using System.Text.RegularExpressions;
using OptiGames.Core.Tweaks;

namespace OptiGames.Core.Services;

/// <summary>
/// Imports the OptiGames power plan and makes it active, reversibly. powercfg keeps plans in
/// its own store rather than a registry value we could author an off-state for, so the only
/// way to undo this is to remember which plan the user was on and put the machine back on it.
/// Both GUIDs are persisted under <see cref="AppPaths.Root"/> so a revert still works after a
/// restart, or after the app is closed and reopened.
/// </summary>
public sealed class PowerPlanService
{
    private const string PowerPlanPayload = "powerplan.pow";

    /// <summary>The friendly name inside powerplan.pow; how we find the plan again in /list.</summary>
    private const string PlanNameMarker = "OptiGames";

    /// <summary>Windows' Balanced plan. Same GUID on every install, so it is a safe fallback.</summary>
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    /// <summary>
    /// powercfg labels its output in the display language ("Power Scheme GUID", "GUID des
    /// Energiesparplans", ...), so every parse here matches the GUID shape and ignores words.
    /// </summary>
    private static readonly Regex GuidPattern =
        new("[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}", RegexOptions.Compiled);

    private static string PreviousFile => AppPaths.File("powerplan-previous.txt");
    private static string ImportedFile => AppPaths.File("powerplan-imported.txt");

    private readonly TweakContext _ctx;
    public PowerPlanService(TweakContext ctx) => _ctx = ctx;

    /// <summary>The tweak stays hidden until a plan file is compiled into the build.</summary>
    public static bool HasPayload => Payload.Exists(PowerPlanPayload);

    /// <summary>
    /// True only while our imported plan is the active one. If the user picks a different plan
    /// in Windows themselves the tweak reads as not applied, which is what they just did.
    /// </summary>
    public bool IsApplied
    {
        get
        {
            var imported = ReadGuid(ImportedFile);
            if (imported is null) return false;

            var active = ActiveSchemeGuid();
            return active is not null && active.Equals(imported, StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool Apply()
    {
        try
        {
            var known = ReadGuid(ImportedFile);
            var previous = ActiveSchemeGuid();

            // Record where the user was before we switch them off it. Re-applying while our own
            // plan is already active must not overwrite that record, or revert would put them
            // straight back onto the plan they asked to leave.
            if (previous is null)
                _ctx.Log.Write("  could not read the active power plan; revert will fall back to Balanced.");
            else if (!previous.Equals(known, StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(PreviousFile, previous);

            // powercfg -import mints a brand new GUID every time, so a machine that has had the
            // plan imported before (by hand, or by the old batch script) ends up with several
            // plans all called OptiGames. Snapshot them first so we can pick out ours by
            // difference rather than by name alone.
            var before = FindPlanGuidsByName();

            var powFile = Payload.Extract(PowerPlanPayload);
            var import = _ctx.Process.Run("powercfg.exe", $"-import \"{powFile}\"");
            if (!import.Success)
            {
                _ctx.Log.Write("  powercfg could not import the power plan.");
                return false;
            }

            // powercfg -import does print the new GUID, but the text around it is localised, so
            // read the plan back out of /list and take whichever one is new.
            var after = FindPlanGuidsByName();
            var printed = GuidPattern.Match(import.StdOut);

            var imported = after.Except(before, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
                           ?? (printed.Success ? printed.Value : null)
                           ?? after.LastOrDefault();

            if (imported is null)
            {
                _ctx.Log.Write($"  imported the plan but could not find \"{PlanNameMarker}\" in powercfg /list.");
                return false;
            }

            File.WriteAllText(ImportedFile, imported);

            var setActive = _ctx.Process.Run("powercfg.exe", $"/setactive {imported}");
            if (!setActive.Success)
            {
                _ctx.Log.Write("  powercfg could not activate the imported plan.");
                return false;
            }

            _ctx.Log.Write($"  power plan {imported} is now active (was {previous ?? "unknown"}).");
            return true;
        }
        catch (Exception ex)
        {
            _ctx.Log.Write($"  power plan apply failed: {ex.Message}");
            return false;
        }
    }

    public bool Revert()
    {
        try
        {
            // Fall back to a fresh /list lookup: the record can be missing if the user cleared
            // %LOCALAPPDATA%, but the plan itself is still sitting in their power options.
            var imported = ReadGuid(ImportedFile) ?? FindPlanGuidsByName().LastOrDefault();
            var previous = ReadGuid(PreviousFile) ?? BalancedGuid;

            // Never switch onto the plan we are about to delete.
            if (previous.Equals(imported, StringComparison.OrdinalIgnoreCase))
                previous = BalancedGuid;

            var setActive = _ctx.Process.Run("powercfg.exe", $"/setactive {previous}");

            // The recorded plan can be gone — the user may have deleted it in Power Options
            // since we noted it. Balanced always exists, so it is a better landing spot than
            // leaving them on the OptiGames plan.
            if (!setActive.Success && !previous.Equals(BalancedGuid, StringComparison.OrdinalIgnoreCase))
            {
                _ctx.Log.Write($"  power plan {previous} is gone; falling back to Balanced.");
                previous = BalancedGuid;
                setActive = _ctx.Process.Run("powercfg.exe", $"/setactive {previous}");
            }

            if (!setActive.Success)
            {
                // powercfg refuses to delete the active plan, so stop here and keep the records —
                // the user can retry once whatever blocked the switch is gone.
                _ctx.Log.Write($"  could not switch back to power plan {previous}; nothing was deleted.");
                return false;
            }

            _ctx.Log.Write($"  power plan {previous} is now active.");

            if (imported is not null)
            {
                var delete = _ctx.Process.Run("powercfg.exe", $"-delete {imported}");
                if (delete.Success) _ctx.Log.Write($"  removed the OptiGames plan ({imported}).");
                else _ctx.Log.Write("  could not delete the OptiGames plan; it is still in Power Options.");
            }

            Forget();
            return true;
        }
        catch (Exception ex)
        {
            _ctx.Log.Write($"  power plan revert failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>The GUID of the plan Windows is running right now, or null if it cannot be read.</summary>
    private string? ActiveSchemeGuid()
    {
        var result = _ctx.Process.Run("powercfg.exe", "/getactivescheme", timeoutMs: 15_000);
        if (!result.Success) return null;

        var match = GuidPattern.Match(result.StdOut);
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// Every plan in powercfg /list whose friendly name mentions OptiGames, in list order. The
    /// name is ours so it survives localisation; only the labels around it change.
    /// </summary>
    private List<string> FindPlanGuidsByName()
    {
        var found = new List<string>();
        var result = _ctx.Process.Run("powercfg.exe", "/list", timeoutMs: 15_000);
        if (!result.Success) return found;

        foreach (var line in result.StdOut.Split('\n'))
        {
            if (line.IndexOf(PlanNameMarker, StringComparison.OrdinalIgnoreCase) < 0) continue;

            var match = GuidPattern.Match(line);
            if (match.Success) found.Add(match.Value);
        }

        return found;
    }

    /// <summary>Reads a persisted GUID, ignoring anything that is not GUID-shaped.</summary>
    private static string? ReadGuid(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var match = GuidPattern.Match(File.ReadAllText(path));
            return match.Success ? match.Value : null;
        }
        catch
        {
            // An unreadable record is the same as no record — the caller falls back to Balanced.
            return null;
        }
    }

    /// <summary>Drops both records once the machine is back on the user's own plan.</summary>
    private static void Forget()
    {
        foreach (var path in new[] { PreviousFile, ImportedFile })
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* a stale record is harmless; the next apply overwrites it */ }
        }
    }
}
