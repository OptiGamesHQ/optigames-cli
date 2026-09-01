using Microsoft.Win32;
using OptiGames.Core.Helpers;

namespace OptiGames.Core.Tweaks;

/// <summary>What a registry value should look like in a given state (on or off).</summary>
public enum RegOp
{
    /// <summary>Write a REG_DWORD.</summary>
    Dword,
    /// <summary>Write a REG_SZ.</summary>
    String,
    /// <summary>Write a REG_BINARY.</summary>
    Binary,
    /// <summary>Remove the value so Windows falls back to its built-in default.</summary>
    DeleteValue,
    /// <summary>Remove the whole key and its children.</summary>
    DeleteKey,
}

/// <summary>One end of a toggle: the operation to perform and the value it writes.</summary>
public sealed record RegState(RegOp Op, object? Value = null)
{
    public static RegState Dword(int v) => new(RegOp.Dword, v);
    public static RegState Str(string v) => new(RegOp.String, v);
    public static RegState Binary(string hex) => new(RegOp.Binary, Convert.FromHexString(hex));
    public static RegState Delete => new(RegOp.DeleteValue);
    public static RegState DeleteKey => new(RegOp.DeleteKey);

    /// <summary>A value computed at apply time — used for the update-pause timestamps.</summary>
    public static RegState Dynamic(Func<string> f) => new(RegOp.String, f);

    /// <summary>Resolves a deferred value. Plain values pass straight through.</summary>
    public object? Resolve() => Value is Func<string> f ? f() : Value;
}

/// <summary>
/// A single registry value under a tweak's control, with an explicit state for both
/// directions. The Off state is what makes every tweak reversible: it is the Windows
/// default, authored per value rather than captured, so a revert is correct even on a
/// machine that was already modified before the tool ever ran.
/// </summary>
public sealed class RegAction
{
    public RegistryHive Hive { get; }
    public string Path { get; }
    public string Name { get; }
    public RegState On { get; }
    public RegState Off { get; }

    /// <summary>
    /// Skips this action entirely when false — used by browser debloat so we only write
    /// policies for browsers that are actually installed.
    /// </summary>
    public Func<bool>? AppliesWhen { get; init; }

    public RegAction(RegistryHive hive, string path, string name, RegState on, RegState off)
    {
        Hive = hive;
        Path = path;
        Name = name;
        On = on;
        Off = off;
    }

    public bool IsRelevant => AppliesWhen is null || AppliesWhen();

    // ---- Construction helpers, one per value type. `off: null` means "delete the value",
    // which is the right revert for anything Windows does not ship with. ----

    public static RegAction Dword(RegistryHive hive, string path, string name, int on, int? off)
        => new(hive, path, name, RegState.Dword(on),
               off is null ? RegState.Delete : RegState.Dword(off.Value));

    public static RegAction Str(RegistryHive hive, string path, string name, string on, string? off)
        => new(hive, path, name, RegState.Str(on),
               off is null ? RegState.Delete : RegState.Str(off));

    public static RegAction Binary(RegistryHive hive, string path, string name, string onHex, string? offHex)
        => new(hive, path, name, RegState.Binary(onHex),
               offHex is null ? RegState.Delete : RegState.Binary(offHex));

    /// <summary>Writes the key's (Default) value; reverting removes the key outright.</summary>
    public static RegAction DefaultValueShim(RegistryHive hive, string path)
        => new(hive, path, "", RegState.Str(""), RegState.DeleteKey);

    /// <summary>A REG_SZ whose on-value is computed when the tweak is applied.</summary>
    public static RegAction Timestamp(RegistryHive hive, string path, string name, Func<string> on)
        => new(hive, path, name, RegState.Dynamic(on), RegState.Delete);

    /// <summary>True when the live registry already matches the given state.</summary>
    public bool Matches(RegistryHelper reg, RegState state)
    {
        var current = reg.GetValue(Hive, Path, Name);
        switch (state.Op)
        {
            case RegOp.DeleteValue:
                return current is null;
            case RegOp.DeleteKey:
                return !reg.KeyExists(Hive, Path);
            case RegOp.Dword:
                return current is not null && SafeInt(current) == (int)state.Resolve()!;
            case RegOp.String:
                return string.Equals(current as string, (string)state.Resolve()!, StringComparison.OrdinalIgnoreCase);
            case RegOp.Binary:
                return current is byte[] b && b.AsSpan().SequenceEqual((byte[])state.Resolve()!);
            default:
                return false;
        }
    }

    public void Write(RegistryHelper reg, RegState state)
    {
        switch (state.Op)
        {
            case RegOp.Dword: reg.SetDword(Hive, Path, Name, (int)state.Resolve()!); break;
            case RegOp.String: reg.SetString(Hive, Path, Name, (string)state.Resolve()!); break;
            case RegOp.Binary: reg.SetBinary(Hive, Path, Name, (byte[])state.Resolve()!); break;
            case RegOp.DeleteValue: reg.DeleteValue(Hive, Path, Name); break;
            case RegOp.DeleteKey: reg.DeleteKeyTree(Hive, Path); break;
        }
    }

    private static int? SafeInt(object o)
    {
        try { return Convert.ToInt32(o); } catch { return null; }
    }

    // ---- Disclosure ----------------------------------------------------------
    // A tool that edits the registry with elevation should be able to show its work
    // before it runs, not only afterwards in a log.

    /// <summary>Full path of the value, e.g. "HKLM\Software\...\AllowGameDVR".</summary>
    public string DisplayPath =>
        $"{ShortHive(Hive)}\\{Path}\\{(Name.Length == 0 ? "(Default)" : Name)}";

    /// <summary>What this value becomes when the tweak is switched on.</summary>
    public string OnText => Describe(On);

    /// <summary>What it is restored to when the tweak is switched back off.</summary>
    public string OffText => Describe(Off);

    private static string Describe(RegState state) => state.Op switch
    {
        RegOp.DeleteValue => "remove value",
        RegOp.DeleteKey => "remove key",
        RegOp.Binary => Convert.ToHexString((byte[])state.Resolve()!),
        RegOp.String => $"\"{state.Resolve()}\"",
        _ => state.Resolve()?.ToString() ?? "—",
    };

    private static string ShortHive(RegistryHive hive) => hive switch
    {
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.ClassesRoot => "HKCR",
        _ => hive.ToString(),
    };
}
