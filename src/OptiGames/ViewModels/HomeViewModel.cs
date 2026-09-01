using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using OptiGames.Core.Services;

namespace OptiGames.ViewModels;

public sealed record SpecLine(string Icon, string Label, string Value);

public sealed class HomeViewModel : PageViewModel
{
    private readonly MainViewModel _main;
    private readonly SystemInfoProvider _info = new();
    private readonly LiveMetrics _metrics = new();

    /// <summary>Samples kept per meter. At one per second that is the last minute.</summary>
    private const int HistoryLength = 60;

    /// <summary>
    /// Sparkline geometry space: x runs 0..1 oldest to newest, y runs top-down over a 38 unit
    /// strip. The view draws these with Shape.Stretch="Fill", which maps the geometry onto the
    /// card's real width and height without scaling the pen, so the units here are only ever
    /// relative. A RenderTransform cannot be used for that — it would scale the 1.5px stroke
    /// along with the points and smear every vertical segment across the card.
    /// </summary>
    private const double SparkHeight = 38;
    private const double SparkInset = 2.5;

    private readonly List<double> _cpuHistory = new(HistoryLength);
    private readonly List<double> _ramHistory = new(HistoryLength);

    private DispatcherTimer? _timer;

    public HomeViewModel(MainViewModel main)
    {
        _main = main;
        GoOptimize = new RelayCommand(() => _main.Navigate<OptimizeViewModel>());
        GoClean = new RelayCommand(() => _main.Navigate<CleanDriveViewModel>());
    }

    public RelayCommand GoOptimize { get; }
    public RelayCommand GoClean { get; }

    public IReadOnlyList<SpecLine> Specs { get; private set; } = Array.Empty<SpecLine>();

    private bool _hasTweaksApplied;
    public bool HasTweaksApplied { get => _hasTweaksApplied; private set => Set(ref _hasTweaksApplied, value); }

    private string _tweakSummary = "Checking…";
    public string TweakSummary { get => _tweakSummary; private set => Set(ref _tweakSummary, value); }

    private string _cleanSummary = "No cleanup yet";
    public string CleanSummary { get => _cleanSummary; private set => Set(ref _cleanSummary, value); }

    private bool _hasRestorePoint;
    public bool HasRestorePoint { get => _hasRestorePoint; private set => Set(ref _hasRestorePoint, value); }

    private string _restoreSummary = "Checking…";
    public string RestoreSummary { get => _restoreSummary; private set => Set(ref _restoreSummary, value); }

    // ---------------------------------------------------------------- live meters

    private double _cpuPercent;
    public double CpuPercent { get => _cpuPercent; private set => Set(ref _cpuPercent, value); }

    private double _ramPercent;
    public double RamPercent { get => _ramPercent; private set => Set(ref _ramPercent, value); }

    private string _cpuText = "—";
    public string CpuText { get => _cpuText; private set => Set(ref _cpuText, value); }

    private string _ramText = "—";
    public string RamText { get => _ramText; private set => Set(ref _ramText, value); }

    /// <summary>Raw history, newest last. Exposed for anything that wants the numbers.</summary>
    public IReadOnlyList<double> CpuHistory => _cpuHistory;
    public IReadOnlyList<double> RamHistory => _ramHistory;

    /// <summary>
    /// Sparkline vertices. Rebuilt as a new collection on every tick: mutating a bound
    /// PointCollection in place does not always redraw the Polyline, and 60 points is nothing.
    /// </summary>
    public PointCollection CpuPoints { get; private set; } = new();
    public PointCollection RamPoints { get; private set; } = new();

    /// <summary>
    /// Pixel width of the sparkline strip, pushed in by the view via OneWayToSource so the
    /// points can be built in real device coordinates.
    ///
    /// This exists so the polyline can be drawn with Stretch="None". Drawing it with
    /// Stretch="Fill" instead makes the line auto-fit whatever range happens to be on screen,
    /// so an idle machine sitting at 3-5% renders as a dramatic mountain range. Pinning y to a
    /// true 0-100% means a flat trace looks flat — a meter that exaggerates is worse than none.
    /// </summary>
    public double SparkWidth
    {
        get => _sparkWidth;
        set
        {
            if (value <= 0 || double.IsNaN(value)) return;
            if (!Set(ref _sparkWidth, value)) return;
            RebuildSparks();
        }
    }
    private double _sparkWidth = 200;

    // ---------------------------------------------------------------- lifecycle

    public override void OnActivated()
    {
        if (Specs.Count == 0)
        {
            var i = _info.Get();
            Specs = new[]
            {
                new SpecLine("I.Board", "Motherboard", i.Motherboard),
                new SpecLine("I.Chip", "CPU", i.Cpu),
                new SpecLine("I.Gpu", "GPU", i.Gpu),
                new SpecLine("I.Ram", "RAM", i.Memory),
                new SpecLine("I.Windows", "Windows", i.Windows),
            };
            Raise(nameof(Specs));
        }

        RefreshSummaries();
        StartMeters();
    }

    private void StartMeters()
    {
        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1),
            };
            _timer.Tick += (_, _) => Tick();
        }

        // The first GetSystemTimes reading has no predecessor and always reads 0%, so burn it
        // here rather than letting it show up as a bogus idle sample in the graph.
        _metrics.Sample();
        Tick();
        _timer.Start();
    }

    /// <summary>
    /// PageViewModel has no deactivation hook, so the meter shuts itself off the first tick
    /// after the user navigates elsewhere; OnActivated starts it again on the way back.
    /// </summary>
    private void Tick()
    {
        if (!ReferenceEquals(_main.Current, this))
        {
            _timer?.Stop();
            return;
        }

        var s = _metrics.Sample();

        CpuPercent = s.CpuPercent;
        RamPercent = s.RamPercent;
        CpuText = s.CpuPercent.ToString("0", CultureInfo.CurrentCulture) + "%";
        RamText = FormatMemory(s.RamUsedBytes, s.RamTotalBytes);

        Push(_cpuHistory, s.CpuPercent);
        Push(_ramHistory, s.RamPercent);

        RebuildSparks();
        Raise(nameof(CpuHistory));
        Raise(nameof(RamHistory));
    }

    private void RebuildSparks()
    {
        CpuPoints = BuildSpark(_cpuHistory);
        RamPoints = BuildSpark(_ramHistory);
        Raise(nameof(CpuPoints));
        Raise(nameof(RamPoints));
    }

    private static void Push(List<double> history, double value)
    {
        history.Add(value);
        if (history.Count > HistoryLength) history.RemoveRange(0, history.Count - HistoryLength);
    }

    /// <summary>
    /// Oldest sample on the left, newest on the right, in real device pixels so the line can be
    /// drawn without any stretch. y maps a fixed 0-100% onto the strip, so half height always
    /// means 50% regardless of what the trace has actually been doing.
    ///
    /// The window fills from the right: with fewer than a minute of samples the line starts
    /// part-way across rather than stretching a handful of readings over the whole card and
    /// implying history that was never recorded.
    /// </summary>
    private PointCollection BuildSpark(IReadOnlyList<double> history)
    {
        var points = new PointCollection(Math.Max(history.Count, 2));
        if (history.Count == 0) return points;

        double usable = SparkHeight - SparkInset * 2;
        double step = _sparkWidth / (HistoryLength - 1);
        int firstIndex = HistoryLength - history.Count;

        for (int i = 0; i < history.Count; i++)
        {
            double x = (firstIndex + i) * step;
            double y = SparkInset + (1.0 - Math.Clamp(history[i], 0, 100) / 100.0) * usable;
            points.Add(new Point(x, y));
        }

        // A single sample has no segment to draw, so extend it into a short flat run.
        if (points.Count == 1) points.Add(new Point(_sparkWidth, points[0].Y));
        return points;
    }

    /// <summary>e.g. "12.4 / 32 GB". Total is rounded because the OS reports slightly less
    /// physical memory than is installed once firmware reservations are taken out.</summary>
    private static string FormatMemory(ulong usedBytes, ulong totalBytes)
    {
        if (totalBytes == 0) return "—";
        const double gb = 1024d * 1024 * 1024;
        return string.Format(CultureInfo.CurrentCulture, "{0:0.0} / {1:0} GB",
            usedBytes / gb, Math.Round(totalBytes / gb));
    }

    // ---------------------------------------------------------------- summaries

    /// <summary>
    /// Reads live state rather than caching, so applying a tweak on the Optimize page and
    /// coming back Home shows the new count.
    /// </summary>
    public void RefreshSummaries()
    {
        int applied = _main.Optimize.AppliedCount;
        int total = _main.Optimize.TotalCount;
        HasTweaksApplied = applied > 0;
        TweakSummary = applied == 0
            ? $"None of {total} tweaks applied"
            : $"{applied} of {total} tweaks applied";

        CleanSummary = _main.Clean.LastCleanupSummary;

        var points = _main.Restore.Points;
        HasRestorePoint = points.Count > 0;
        RestoreSummary = points.Count == 0
            ? "No restore point yet"
            : $"Last: {points[0].CreatedAt:d MMM yyyy, HH:mm}";
    }
}
