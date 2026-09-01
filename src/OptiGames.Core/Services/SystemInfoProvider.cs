using System.Management;

namespace OptiGames.Core.Services;

public sealed record SystemInfo(
    string Motherboard,
    string Cpu,
    string Gpu,
    string Memory,
    string Windows);

public sealed record DriveInfoLine(
    string Letter,
    string Label,
    string MediaType,
    long TotalBytes,
    long FreeBytes)
{
    public double UsedFraction => TotalBytes == 0 ? 0 : 1.0 - (double)FreeBytes / TotalBytes;
}

/// <summary>
/// The hardware summary shown on Home. Every field degrades to "Unknown" rather than
/// throwing — WMI is missing or locked down often enough that a crash here is unacceptable.
/// </summary>
public sealed class SystemInfoProvider
{
    /// <summary>Cached because the Home page rebinds on every navigation.</summary>
    private static SystemInfo? _cached;

    public SystemInfo Get() => _cached ??= Load();

    private static SystemInfo Load() => new(
        Motherboard: Motherboard(),
        Cpu: Hardware.QueryOne("SELECT Name FROM Win32_Processor", "Name")?.Trim() ?? "Unknown",
        Gpu: Hardware.Gpus.Count > 0 ? string.Join(", ", Hardware.Gpus) : "Unknown",
        Memory: Memory(),
        Windows: WindowsVersion());

    private static string Motherboard()
    {
        var manufacturer = Hardware.QueryOne("SELECT Manufacturer FROM Win32_BaseBoard", "Manufacturer");
        var product = Hardware.QueryOne("SELECT Product FROM Win32_BaseBoard", "Product");
        var joined = string.Join(", ", new[] { manufacturer, product }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return joined.Length > 0 ? joined : "Unknown";
    }

    /// <summary>Reports total size, module count and configured speed, e.g. "16 GB (2x8GB) 3200 MHz".</summary>
    private static string Memory()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Capacity, ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");
            var modules = searcher.Get().Cast<ManagementObject>().ToList();
            if (modules.Count == 0) return "Unknown";

            long totalBytes = modules.Sum(m => Convert.ToInt64(m["Capacity"] ?? 0L));
            var sizes = modules
                .Select(m => Convert.ToInt64(m["Capacity"] ?? 0L) / 1024 / 1024 / 1024)
                .GroupBy(g => g)
                .Select(g => $"{g.Count()}x{g.Key}GB");

            // ConfiguredClockSpeed is what the modules actually run at; Speed is the rated
            // maximum, which is misleading when XMP is off.
            var speed = modules
                .Select(m => Convert.ToInt32(m["ConfiguredClockSpeed"] ?? m["Speed"] ?? 0))
                .DefaultIfEmpty(0)
                .Max();

            var total = $"{totalBytes / 1024 / 1024 / 1024} GB";
            var detail = string.Join(" + ", sizes);
            return speed > 0 ? $"{total} ({detail}) {speed} MHz" : $"{total} ({detail})";
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string WindowsVersion()
    {
        var caption = Hardware.QueryOne("SELECT Caption FROM Win32_OperatingSystem", "Caption")?.Trim();
        var build = Environment.OSVersion.Version.Build;
        // Windows 11 reports itself as 10.0 with a build number of 22000 or higher.
        var name = caption ?? (build >= 22000 ? "Windows 11" : "Windows 10");
        if (build >= 22000 && name.Contains("Windows 10"))
            name = name.Replace("Windows 10", "Windows 11");
        return $"{name} (build {build})";
    }

    /// <summary>Fixed drives with capacity and media type, for the Clean Drive page.</summary>
    public IReadOnlyList<DriveInfoLine> Drives()
    {
        var mediaByLetter = MediaTypes();

        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d =>
            {
                var letter = d.Name.TrimEnd('\\');
                return new DriveInfoLine(
                    letter,
                    string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel,
                    mediaByLetter.GetValueOrDefault(letter, "Drive"),
                    d.TotalSize,
                    d.TotalFreeSpace);
            })
            .ToList();
    }

    /// <summary>
    /// Maps drive letters to SSD/HDD via MSFT_PhysicalDisk. Not available on every SKU, so a
    /// miss just leaves the drive labelled generically.
    /// </summary>
    private static Dictionary<string, string> MediaTypes()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"\\.\root\microsoft\windows\storage", "SELECT MediaType, DeviceId FROM MSFT_PhysicalDisk");

            var byDeviceId = searcher.Get().Cast<ManagementObject>().ToDictionary(
                o => o["DeviceId"]?.ToString() ?? "",
                o => Convert.ToInt32(o["MediaType"] ?? 0) switch
                {
                    3 => "HDD",
                    4 => "SSD",
                    5 => "SCM",
                    _ => "Drive",
                });

            // Walk partition -> disk so each letter inherits its physical disk's media type.
            using var partitions = new ManagementObjectSearcher(
                @"\\.\root\microsoft\windows\storage", "SELECT DriveLetter, DiskNumber FROM MSFT_Partition");

            foreach (var p in partitions.Get().Cast<ManagementObject>())
            {
                var letter = p["DriveLetter"]?.ToString();
                if (string.IsNullOrWhiteSpace(letter) || letter == "\0") continue;
                var disk = p["DiskNumber"]?.ToString() ?? "";
                if (byDeviceId.TryGetValue(disk, out var media))
                    result[$"{letter}:"] = media;
            }
        }
        catch
        {
            // Storage WMI namespace missing — every drive stays labelled "Drive".
        }
        return result;
    }
}
