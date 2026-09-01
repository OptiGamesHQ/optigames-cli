using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using OptiGames.Core.Helpers;
using Microsoft.Win32;

namespace OptiGames.Core.Services;

public sealed record RestorePoint(int SequenceNumber, string Description, DateTime CreatedAt);

/// <summary>
/// Windows System Restore: create a checkpoint, list what exists, roll back to one, and
/// delete one. This is the whole-system safety net that sits underneath per-tweak revert.
/// </summary>
public sealed class RestorePointService
{
    private readonly ProcessRunner _proc;
    private readonly RegistryHelper _reg;
    private readonly ILogSink _log;

    private const string RestoreKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
    private const string FrequencyValue = "SystemRestorePointCreationFrequency";

    public RestorePointService(ProcessRunner proc, RegistryHelper reg, ILogSink log)
    {
        _proc = proc;
        _reg = reg;
        _log = log;
    }

    /// <summary>
    /// Enables System Protection, lifts the once-per-24-hours throttle for the duration of
    /// the call, and checkpoints. The throttle is always put back, even on failure —
    /// leaving it at 0 would let anything spam restore points forever.
    /// </summary>
    public bool Create(string description)
    {
        _log.Write($"Creating restore point: {description}");
        var drive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

        try
        {
            _reg.SetDword(RegistryHive.LocalMachine, RestoreKey, FrequencyValue, 0);

            var script =
                $"Enable-ComputerRestore -Drive '{drive}'; " +
                $"Checkpoint-Computer -Description '{description.Replace("'", "''")}' " +
                "-RestorePointType 'MODIFY_SETTINGS'";

            var result = _proc.PowerShell(script, timeoutMs: 300_000);

            if (result.Success) _log.Write("Restore point created.");
            else _log.Write("Could not create a restore point — System Protection may be disabled by policy.");

            return result.Success;
        }
        finally
        {
            _reg.DeleteValue(RegistryHive.LocalMachine, RestoreKey, FrequencyValue);
        }
    }

    /// <summary>Every restore point on the system, newest first.</summary>
    public IReadOnlyList<RestorePoint> List()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"\\.\root\default", "SELECT * FROM SystemRestore");

            return searcher.Get()
                .Cast<ManagementObject>()
                .Select(o => new RestorePoint(
                    Convert.ToInt32(o["SequenceNumber"]),
                    o["Description"]?.ToString() ?? "(no description)",
                    ParseWmiDate(o["CreationTime"]?.ToString())))
                .OrderByDescending(p => p.CreatedAt)
                .ToList();
        }
        catch (Exception ex)
        {
            _log.Write($"Could not read restore points: {ex.Message}");
            return Array.Empty<RestorePoint>();
        }
    }

    /// <summary>
    /// Rolls the machine back. Windows reboots to do the work, so this does not return in
    /// any meaningful sense — the caller should have confirmed with the user first.
    /// </summary>
    public bool Restore(int sequenceNumber)
    {
        _log.Write($"Restoring to point {sequenceNumber}. Windows will restart.");
        var result = _proc.PowerShell($"Restore-Computer -RestorePoint {sequenceNumber} -Confirm:$false");
        if (!result.Success) _log.Write("Restore failed to start.");
        return result.Success;
    }

    [DllImport("srclient.dll", SetLastError = true)]
    private static extern int SRRemoveRestorePoint(int index);

    /// <summary>Deletes one restore point. srclient is the only API that can do this per-point.</summary>
    public bool Delete(int sequenceNumber)
    {
        try
        {
            int rc = SRRemoveRestorePoint(sequenceNumber);
            if (rc == 0) { _log.Write($"Deleted restore point {sequenceNumber}."); return true; }
            _log.Write($"Could not delete restore point {sequenceNumber} (code {rc}).");
            return false;
        }
        catch (Exception ex)
        {
            _log.Write($"Could not delete restore point: {ex.Message}");
            return false;
        }
    }

    /// <summary>Opens the Windows System Restore wizard.</summary>
    public void OpenWizard() => _proc.Run("rstrui.exe", "");

    /// <summary>WMI reports "20260901022530.000000-000"; anything unparseable becomes MinValue.</summary>
    private static DateTime ParseWmiDate(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length < 14) return DateTime.MinValue;
        return DateTime.TryParseExact(raw[..14], "yyyyMMddHHmmss",
                                      CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt
            : DateTime.MinValue;
    }
}
