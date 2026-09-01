using System.Runtime.InteropServices;

namespace OptiGames.Core.Services;

/// <summary>One sweepable location, sized so the user can see what they are about to free.</summary>
public sealed class CleanupTarget
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public bool SelectedByDefault { get; init; } = true;

    /// <summary>Bytes currently occupied. Filled in by a scan.</summary>
    public long Bytes { get; set; }

    internal Func<long>? Measure { get; init; }
    internal Action<ILogSink>? Clean { get; init; }
}

/// <summary>
/// Finds and removes the caches Windows never cleans up on its own. Deliberately excludes
/// anything the user would miss — no Downloads folder, no browser profiles, no pagefile.
/// </summary>
public sealed class DriveCleanupService
{
    private readonly ILogSink _log;
    public DriveCleanupService(ILogSink log) => _log = log;

    // ---- Recycle Bin interop ----

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);

    private const uint SHERB_NOCONFIRMATION = 0x1;
    private const uint SHERB_NOPROGRESSUI = 0x2;
    private const uint SHERB_NOSOUND = 0x4;

    public IReadOnlyList<CleanupTarget> BuildTargets()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var userTemp = Path.GetTempPath();
        var winTemp = Path.Combine(windows, "Temp");
        var prefetch = Path.Combine(windows, "Prefetch");
        var updateCache = Path.Combine(windows, "SoftwareDistribution", "Download");
        var delivery = Path.Combine(windows, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization");
        var thumbs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Explorer");
        var dumps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrashDumps");

        return new List<CleanupTarget>
        {
            new()
            {
                Id = "user-temp", Name = "Temporary Files",
                Description = "Your user temp folder. Installers and apps leave files here and never return for them.",
                Measure = () => DirectorySize(userTemp),
                Clean = log => EmptyDirectory(userTemp, log),
            },
            new()
            {
                Id = "windows-temp", Name = "Windows Temp",
                Description = "The system-wide temp folder used by Windows components and drivers.",
                Measure = () => DirectorySize(winTemp),
                Clean = log => EmptyDirectory(winTemp, log),
            },
            new()
            {
                Id = "recycle-bin", Name = "Recycle Bin",
                Description = "Everything you have already deleted, on every drive.",
                Measure = RecycleBinSize,
                Clean = EmptyRecycleBin,
            },
            new()
            {
                Id = "update-cache", Name = "Windows Update Cache",
                Description = "Installers for updates that are already applied. Windows re-downloads if it ever needs them.",
                Measure = () => DirectorySize(updateCache),
                Clean = log => EmptyDirectory(updateCache, log),
            },
            new()
            {
                Id = "delivery-optimization", Name = "Delivery Optimization Cache",
                Description = "Update chunks Windows keeps to share with other PCs on your network.",
                Measure = () => DirectorySize(delivery),
                Clean = log => EmptyDirectory(delivery, log),
            },
            new()
            {
                Id = "crash-dumps", Name = "Crash Dumps",
                Description = "Memory dumps written when an application crashed. Only useful to a developer.",
                Measure = () => DirectorySize(dumps),
                Clean = log => EmptyDirectory(dumps, log),
            },
            new()
            {
                Id = "prefetch", Name = "Prefetch Data",
                Description = "App launch-order hints. Windows rebuilds these; expect slightly slower first launches afterwards.",
                SelectedByDefault = false,
                Measure = () => DirectorySize(prefetch),
                Clean = log => EmptyDirectory(prefetch, log),
            },
            new()
            {
                Id = "thumbnails", Name = "Thumbnail Cache",
                Description = "Cached image and video previews. Explorer regenerates them on demand.",
                SelectedByDefault = false,
                Measure = () => FilesMatching(thumbs, "thumbcache_*.db").Sum(SafeLength),
                Clean = log => DeleteFiles(FilesMatching(thumbs, "thumbcache_*.db"), log),
            },
        };
    }

    /// <summary>Sizes every target. Slow (walks directories), so callers run it off the UI thread.</summary>
    public void Scan(IEnumerable<CleanupTarget> targets, CancellationToken cancel = default)
    {
        foreach (var t in targets)
        {
            cancel.ThrowIfCancellationRequested();
            t.Bytes = t.Measure?.Invoke() ?? 0;
        }
    }

    /// <summary>Cleans the given targets and returns the total bytes they were holding.</summary>
    public long Clean(IEnumerable<CleanupTarget> targets,
                      IProgress<(double Fraction, string Label)>? progress = null,
                      CancellationToken cancel = default)
    {
        var list = targets.ToList();
        long freed = 0;

        for (int i = 0; i < list.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var t = list[i];
            progress?.Report(((double)i / list.Count, t.Name));

            long before = t.Bytes;
            _log.Write($"Cleaning {t.Name} ({FormatBytes(before)})");
            try { t.Clean?.Invoke(_log); }
            catch (Exception ex) { _log.Write($"  partly skipped: {ex.Message}"); }

            long after = t.Measure?.Invoke() ?? 0;
            t.Bytes = after;
            freed += Math.Max(0, before - after);
        }

        progress?.Report((1.0, "Done"));
        _log.Write($"Freed {FormatBytes(freed)}.");
        return freed;
    }

    // ---------------------------------------------------------------- measuring

    private static long DirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        var stack = new Stack<string>();
        stack.Push(path);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir)) total += SafeLength(f);
                foreach (var d in Directory.EnumerateDirectories(dir)) stack.Push(d);
            }
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { }
        }
        return total;
    }

    private static long SafeLength(string file)
    {
        try { return new FileInfo(file).Length; } catch { return 0; }
    }

    private static IEnumerable<string> FilesMatching(string dir, string pattern)
    {
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        try { return Directory.EnumerateFiles(dir, pattern).ToList(); }
        catch { return Array.Empty<string>(); }
    }

    private static long RecycleBinSize()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
    }

    // ---------------------------------------------------------------- cleaning

    /// <summary>
    /// Removes a directory's contents but keeps the directory. Files in use are skipped —
    /// that is normal for temp folders and is not an error worth surfacing.
    /// </summary>
    private static void EmptyDirectory(string path, ILogSink log)
    {
        if (!Directory.Exists(path)) return;
        int skipped = 0;

        foreach (var file in SafeEnumerate(() => Directory.EnumerateFiles(path)))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); }
            catch { skipped++; }
        }

        foreach (var dir in SafeEnumerate(() => Directory.EnumerateDirectories(path)))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { skipped++; }
        }

        if (skipped > 0) log.Write($"  {skipped} item(s) in use, left alone");
    }

    private static void DeleteFiles(IEnumerable<string> files, ILogSink log)
    {
        int skipped = 0;
        foreach (var f in files)
        {
            try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }
            catch { skipped++; }
        }
        if (skipped > 0) log.Write($"  {skipped} file(s) locked, left alone");
    }

    private static void EmptyRecycleBin(ILogSink log)
    {
        int rc = SHEmptyRecycleBin(IntPtr.Zero, null,
                                   SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
        // 0 = emptied, -2147418113 (E_UNEXPECTED) = it was already empty.
        if (rc != 0 && rc != unchecked((int)0x8000FFFF))
            log.Write($"  Recycle Bin returned 0x{rc:X8}");
    }

    private static IEnumerable<string> SafeEnumerate(Func<IEnumerable<string>> get)
    {
        try { return get().ToList(); }
        catch { return Array.Empty<string>(); }
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : $"{v:0.#} {units[u]}";
    }
}
