using System.Management;

namespace OptiGames.Core.Services;

/// <summary>
/// Cheap, cached hardware facts the catalog and Home page both need. WMI queries are slow
/// enough (100–300 ms each) that they must not run per repaint.
/// </summary>
public static class Hardware
{
    private static readonly Lazy<IReadOnlyList<string>> _gpus = new(() =>
        Query("SELECT Name FROM Win32_VideoController", "Name"));

    public static IReadOnlyList<string> Gpus => _gpus.Value;

    public static bool HasNvidiaGpu =>
        Gpus.Any(g => g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));

    /// <summary>Runs a WMI query and returns one property per row. Returns empty on failure.</summary>
    public static IReadOnlyList<string> Query(string wql, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            return searcher.Get()
                .Cast<ManagementObject>()
                .Select(o => o[property]?.ToString() ?? "")
                .Where(s => s.Length > 0)
                .ToList();
        }
        catch
        {
            // WMI is disabled or the class is missing on this SKU — the UI shows "Unknown".
            return Array.Empty<string>();
        }
    }

    public static string? QueryOne(string wql, string property) => Query(wql, property).FirstOrDefault();
}
