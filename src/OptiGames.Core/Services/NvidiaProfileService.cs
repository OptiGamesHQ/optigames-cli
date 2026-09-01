using System.Text.RegularExpressions;
using OptiGames.Core.Tweaks;

namespace OptiGames.Core.Services;

/// <summary>
/// Drives NVIDIA Profile Inspector to import a tuned global driver profile.
///
/// Inspector has no export switch — its only CLI options are -silentImport, -silent,
/// -createCSN, -showOnlyCSN and -disableScan — so there is no way to dump what the user had
/// before, and writing an "NVIDIA defaults" profile by hand would leave the GPU in a state the
/// user believes is stock but is not. Instead the driver's own profile database is byte-copied
/// aside before the first import, and revert puts those exact files back.
/// </summary>
public sealed class NvidiaProfileService
{
    private const string InspectorPayload = "inspector.exe";
    private const string ProfilePayload = "optigames.nip";

    /// <summary>The driver's profile store. Restoring these three is a full profile rollback.</summary>
    private static readonly string[] DrsFiles = { "nvdrsdb0.bin", "nvdrsdb1.bin", "nvdrssel.bin" };

    /// <summary>Holds the .bin files open, so they cannot be overwritten while it is running.</summary>
    private const string DisplayService = "NVDisplay.ContainerLocalSystem";

    private const string DriverVersionQuery =
        "SELECT DriverVersion FROM Win32_VideoController WHERE Name LIKE '%NVIDIA%'";

    /// <summary>
    /// ProgramData can be relocated, so this is resolved rather than hard-coded to C:\ProgramData.
    /// </summary>
    private static string DrsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "NVIDIA Corporation", "Drs");

    private static string BackupDir => Path.Combine(AppPaths.Ensure(), "nvidia-drs-backup");
    private static string DriverVersionFile => Path.Combine(BackupDir, "driver-version.txt");

    /// <summary>Marker written next to the profile so the UI can show the toggle state.</summary>
    private static string AppliedMarker => AppPaths.File("nvidia-profile.applied");

    private readonly TweakContext _ctx;
    public NvidiaProfileService(TweakContext ctx) => _ctx = ctx;

    /// <summary>The tweak stays hidden until a profile payload is compiled into the build.</summary>
    public static bool HasProfilePayload => Payload.Exists(ProfilePayload);

    /// <summary>The driver keeps profiles in its own database, so there is nothing to read back.</summary>
    public bool IsApplied => File.Exists(AppliedMarker);

    public bool Apply()
    {
        try
        {
            BackupProfileDatabase();

            var inspector = Payload.Extract(InspectorPayload);
            var profile = Payload.Extract(ProfilePayload);

            _ctx.Log.Write("NVIDIA Profile Inspector: importing the OptiGames profile.");
            var result = _ctx.Process.Run(inspector, $"-silentImport -silent \"{profile}\"", timeoutMs: 60_000);

            if (!result.Success)
            {
                _ctx.Log.Write("  NVIDIA profile import failed — is the NVIDIA driver installed?");
                return false;
            }

            File.WriteAllText(AppliedMarker, DateTime.Now.ToString("o"));
            _ctx.Log.Write("  NVIDIA profile applied.");
            return true;
        }
        catch (Exception ex)
        {
            _ctx.Log.Write($"  NVIDIA profile apply failed: {ex.Message}");
            return false;
        }
    }

    public bool Revert()
    {
        try
        {
            if (!HasBackup)
            {
                _ctx.Log.Write("No NVIDIA profile backup exists, so there is nothing to restore.");
                _ctx.Log.Write("Reset the driver yourself: NVIDIA Control Panel > Manage 3D Settings > Restore Defaults.");
                return false;
            }

            var recorded = ReadRecordedDriverVersion();
            var installed = InstalledDriverVersion();

            // A profile database belongs to the driver build that wrote it. Pushing one from a
            // different build back over the live store can corrupt it, and a corrupt profile
            // store is a much worse outcome than leaving the tuned profile in place.
            if (string.IsNullOrEmpty(recorded) || string.IsNullOrEmpty(installed) ||
                !recorded.Equals(installed, StringComparison.OrdinalIgnoreCase))
            {
                _ctx.Log.Write($"NVIDIA driver version does not match the backup " +
                               $"(backup: {Describe(recorded)}, installed: {Describe(installed)}).");
                _ctx.Log.Write("The saved profile database belongs to a different driver, so it was NOT restored.");
                _ctx.Log.Write("Reset the driver yourself: NVIDIA Control Panel > Manage 3D Settings > Restore Defaults.");
                return false;
            }

            return RestoreProfileDatabase();
        }
        catch (Exception ex)
        {
            _ctx.Log.Write($"  NVIDIA profile revert failed: {ex.Message}");
            return false;
        }
    }

    private static bool HasBackup =>
        File.Exists(DriverVersionFile) && DrsFiles.Any(f => File.Exists(Path.Combine(BackupDir, f)));

    // --------------------------------------------------------------------- Backup / restore

    /// <summary>
    /// Copies the driver's profile database aside exactly once. Anything after the first apply
    /// would capture our own tuned profile, which would make revert a no-op.
    /// </summary>
    private void BackupProfileDatabase()
    {
        if (HasBackup)
        {
            _ctx.Log.Write("  NVIDIA profile backup already exists — keeping the original.");
            return;
        }

        var source = DrsDirectory;
        if (!Directory.Exists(source))
        {
            _ctx.Log.Write($"  no NVIDIA profile database at {source}; revert will have nothing to restore.");
            return;
        }

        Directory.CreateDirectory(BackupDir);

        int copied = 0;
        foreach (var name in DrsFiles)
        {
            var from = Path.Combine(source, name);
            if (!File.Exists(from)) continue;

            try
            {
                File.Copy(from, Path.Combine(BackupDir, name), overwrite: true);
                copied++;
            }
            catch (Exception ex)
            {
                _ctx.Log.Write($"  could not back up {name}: {ex.Message}");
            }
        }

        if (copied == 0)
        {
            _ctx.Log.Write("  nothing was backed up; revert will refuse rather than guess defaults.");
            return;
        }

        // Written last, and only on success: its presence is what marks the backup usable.
        File.WriteAllText(DriverVersionFile, InstalledDriverVersion() ?? "");
        _ctx.Log.Write($"  backed up {copied} NVIDIA profile file(s) to {BackupDir}.");
    }

    private bool RestoreProfileDatabase()
    {
        var target = DrsDirectory;

        _ctx.Log.Write($"Stopping {DisplayService} so the profile database is not locked. " +
                       "The screen may blank for a moment.");
        StopDisplayService();

        try
        {
            Directory.CreateDirectory(target);

            int restored = 0;
            foreach (var name in DrsFiles)
            {
                var from = Path.Combine(BackupDir, name);
                if (!File.Exists(from)) continue;

                File.Copy(from, Path.Combine(target, name), overwrite: true);
                _ctx.Log.Write($"  restored {name}.");
                restored++;
            }

            if (restored == 0)
            {
                _ctx.Log.Write("  the backup folder held no profile files; nothing was restored.");
                return false;
            }

            if (File.Exists(AppliedMarker)) File.Delete(AppliedMarker);
            _ctx.Log.Write("  NVIDIA profile database restored to its pre-OptiGames state.");
            return true;
        }
        catch (Exception ex)
        {
            _ctx.Log.Write($"  could not restore the NVIDIA profile database: {ex.Message}");
            return false;
        }
        finally
        {
            // Always bring the service back, even when the copy failed — leaving it stopped
            // costs the user the NVIDIA Control Panel and the driver overlay until they reboot.
            StartDisplayService();
        }
    }

    // ------------------------------------------------------------------------- Service control

    /// <summary>
    /// sc returns the moment the stop is queued, so this polls until the service reports
    /// STOPPED. Copying over files the container still has open would fail with a share lock.
    /// </summary>
    private void StopDisplayService()
    {
        _ctx.Process.Run("sc.exe", $"stop {DisplayService}", timeoutMs: 30_000);

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (IsDisplayServiceStopped())
            {
                _ctx.Log.Write($"  {DisplayService} stopped.");
                return;
            }

            Thread.Sleep(500);
        }

        _ctx.Log.Write($"  {DisplayService} did not stop within 20s; attempting the restore anyway.");
    }

    private void StartDisplayService()
    {
        var result = _ctx.Process.Run("sc.exe", $"start {DisplayService}", timeoutMs: 30_000);
        _ctx.Log.Write(result.Success
            ? $"  {DisplayService} restarted."
            : $"  {DisplayService} did not restart; a reboot will bring it back.");
    }

    /// <summary>
    /// sc query prints "STATE : 1  STOPPED". The word is localised but the numeric state is
    /// not, so match the number and require whitespace after it (TYPE prints 10, 20, ...).
    /// </summary>
    private static readonly Regex StoppedState = new(@":\s*1\s", RegexOptions.Compiled);

    private bool IsDisplayServiceStopped()
    {
        var result = _ctx.Process.Run("sc.exe", $"query {DisplayService}", timeoutMs: 15_000);

        // A missing or unqueryable service is not holding the files open, so treat it as stopped.
        if (!result.Success) return true;

        return StoppedState.IsMatch(result.StdOut);
    }

    // --------------------------------------------------------------------------------- Version

    private static string? InstalledDriverVersion() =>
        Hardware.QueryOne(DriverVersionQuery, "DriverVersion");

    private static string? ReadRecordedDriverVersion()
    {
        try
        {
            return File.Exists(DriverVersionFile) ? File.ReadAllText(DriverVersionFile).Trim() : null;
        }
        catch
        {
            // Unreadable record means we cannot prove the driver matches, so revert must refuse.
            return null;
        }
    }

    private static string Describe(string? version) => string.IsNullOrEmpty(version) ? "unknown" : version;
}
