using System.Security;
using Microsoft.Win32;

namespace OptiGames.Core.Helpers;

/// <summary>
/// Typed registry access with logging. Takes the hive explicitly so it can be exercised
/// against HKCU without administrator rights, while system tweaks use HKLM.
/// Always uses the 64-bit view to avoid WOW6432 redirection surprises.
/// </summary>
public sealed class RegistryHelper
{
    private readonly ILogSink _log;
    public RegistryHelper(ILogSink log) => _log = log;

    private static RegistryKey BaseKey(RegistryHive hive) =>
        RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

    /// <summary>Reads a raw value, or null if the key/value is absent or inaccessible.</summary>
    public object? GetValue(RegistryHive hive, string subKey, string name)
        => ReadSafe<object?>(hive, subKey, key => key.GetValue(name, null), null);

    public int? GetDword(RegistryHive hive, string subKey, string name)
    {
        var raw = GetValue(hive, subKey, name);
        if (raw is null) return null;
        try { return Convert.ToInt32(raw); } catch { return null; }
    }

    public string? GetString(RegistryHive hive, string subKey, string name)
        => GetValue(hive, subKey, name) as string;

    public byte[]? GetBinary(RegistryHive hive, string subKey, string name)
        => GetValue(hive, subKey, name) as byte[];

    public bool ValueExists(RegistryHive hive, string subKey, string name)
        => GetValue(hive, subKey, name) is not null;

    public bool KeyExists(RegistryHive hive, string subKey)
        => ReadSafe(hive, subKey, _ => true, false);

    /// <summary>
    /// Opens a subkey read-only and applies <paramref name="read"/>. Returns
    /// <paramref name="fallback"/> if the key is missing or access is denied (some keys have
    /// ACLs that block even administrators — we must skip, not crash).
    /// </summary>
    private T ReadSafe<T>(RegistryHive hive, string subKey, Func<RegistryKey, T> read, T fallback)
    {
        try
        {
            using var baseKey = BaseKey(hive);
            using var key = baseKey.OpenSubKey(subKey, writable: false);
            return key is null ? fallback : read(key);
        }
        catch (SecurityException) { return fallback; }
        catch (UnauthorizedAccessException) { return fallback; }
        catch (IOException) { return fallback; }
    }

    public void SetDword(RegistryHive hive, string subKey, string name, int value)
        => Set(hive, subKey, name, value, RegistryValueKind.DWord, value.ToString());

    public void SetString(RegistryHive hive, string subKey, string name, string value)
        => Set(hive, subKey, name, value, RegistryValueKind.String, $"\"{value}\"");

    public void SetBinary(RegistryHive hive, string subKey, string name, byte[] value)
        => Set(hive, subKey, name, value, RegistryValueKind.Binary, Convert.ToHexString(value));

    private void Set(RegistryHive hive, string subKey, string name, object value,
                     RegistryValueKind kind, string display)
    {
        using var baseKey = BaseKey(hive);
        using var key = baseKey.CreateSubKey(subKey, writable: true);
        // An empty name writes the key's (Default) value, which is how the Windows 10
        // right-click menu shim is installed.
        key.SetValue(name, value, kind);
        var shown = name.Length == 0 ? "(Default)" : name;
        _log.Write($"  reg: {HiveName(hive)}\\{subKey}\\{shown} = {display}");
    }

    /// <summary>Deletes a value if present. Never throws if it is already gone.</summary>
    public void DeleteValue(RegistryHive hive, string subKey, string name)
    {
        try
        {
            using var baseKey = BaseKey(hive);
            using var key = baseKey.OpenSubKey(subKey, writable: true);
            if (key is null) return;
            if (key.GetValue(name, null) is null) return;
            key.DeleteValue(name, throwOnMissingValue: false);
            _log.Write($"  reg: deleted {HiveName(hive)}\\{subKey}\\{(name.Length == 0 ? "(Default)" : name)}");
        }
        catch (SecurityException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Deletes a key and everything under it. Never throws if it is already gone.</summary>
    public void DeleteKeyTree(RegistryHive hive, string subKey)
    {
        try
        {
            using var baseKey = BaseKey(hive);
            using (var probe = baseKey.OpenSubKey(subKey))
                if (probe is null) return;
            baseKey.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            _log.Write($"  reg: deleted key {HiveName(hive)}\\{subKey}");
        }
        catch (SecurityException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string HiveName(RegistryHive hive) => hive switch
    {
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.ClassesRoot => "HKCR",
        _ => hive.ToString(),
    };
}
