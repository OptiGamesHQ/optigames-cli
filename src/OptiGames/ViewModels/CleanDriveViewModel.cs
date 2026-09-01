using System.Collections.ObjectModel;
using OptiGames.Core.Services;

namespace OptiGames.ViewModels;

public sealed class CleanupItemViewModel : ObservableObject
{
    private readonly CleanDriveViewModel _owner;
    public CleanupTarget Target { get; }

    public CleanupItemViewModel(CleanupTarget target, CleanDriveViewModel owner)
    {
        Target = target;
        _owner = owner;
        _isSelected = target.SelectedByDefault;
    }

    public string Name => Target.Name;
    public string Description => Target.Description;

    public string SizeText => Target.Bytes == 0 ? "Empty" : DriveCleanupService.FormatBytes(Target.Bytes);
    public bool HasContent => Target.Bytes > 0;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (Set(ref _isSelected, value)) _owner.RecalcSelection(); }
    }

    public void RaiseSize()
    {
        Raise(nameof(SizeText));
        Raise(nameof(HasContent));
    }

    /// <summary>
    /// Re-applies the default selection now that the size is known. Construction happens before
    /// the scan, so a target that turns out to hold nothing would otherwise sit ticked and
    /// contribute a row of noise to a list the user is meant to be able to skim.
    /// </summary>
    public void ResetSelection()
    {
        _isSelected = Target.SelectedByDefault && Target.Bytes > 0;
        Raise(nameof(IsSelected));
    }
}

public sealed class DriveViewModel
{
    public required string Header { get; init; }
    public required string Sub { get; init; }
    public required double UsedFraction { get; init; }
    public required string FreeText { get; init; }
}

public sealed class CleanDriveViewModel : PageViewModel
{
    private readonly MainViewModel _main;
    private readonly DriveCleanupService _service;
    private readonly SystemInfoProvider _info = new();
    private bool _scannedOnce;

    public CleanDriveViewModel(MainViewModel main, DriveCleanupService service)
    {
        _main = main;
        _service = service;

        Items = new ObservableCollection<CleanupItemViewModel>(
            service.BuildTargets().Select(t => new CleanupItemViewModel(t, this)));

        ScanCommand = new RelayCommand(async () => await ScanAsync(), () => !IsBusy);
        CleanCommand = new RelayCommand(async () => await CleanAsync(), () => SelectedBytes > 0 && !IsBusy);
    }

    public ObservableCollection<CleanupItemViewModel> Items { get; }
    public ObservableCollection<DriveViewModel> Drives { get; } = new();

    public RelayCommand ScanCommand { get; }
    public RelayCommand CleanCommand { get; }

    private long _selectedBytes;
    public long SelectedBytes
    {
        get => _selectedBytes;
        private set
        {
            if (!Set(ref _selectedBytes, value)) return;
            Raise(nameof(SelectedText));
            Raise(nameof(CleanLabel));
            Raise(nameof(HasSelection));
        }
    }

    public bool HasSelection => SelectedBytes > 0;
    public string SelectedText => DriveCleanupService.FormatBytes(SelectedBytes);
    public string CleanLabel => SelectedBytes == 0 ? "Nothing to clean" : $"Clean {SelectedText}";

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (Set(ref _isBusy, value)) Raise(nameof(IsIdle)); }
    }

    public bool IsIdle => !IsBusy;

    private double _progress;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    private string _statusLine = "Scanning…";
    public string StatusLine { get => _statusLine; private set => Set(ref _statusLine, value); }

    public string LastCleanupSummary { get; private set; } = "No cleanup yet";

    public override void OnActivated()
    {
        RefreshDrives();
        // Sizes go stale the moment you use the machine, but re-walking every temp folder on
        // each navigation is wasteful. Scan once, then only on demand.
        if (!_scannedOnce) _ = ScanAsync();
    }

    private void RefreshDrives()
    {
        Drives.Clear();
        foreach (var d in _info.Drives())
        {
            Drives.Add(new DriveViewModel
            {
                Header = $"{d.Letter}  {d.Label}",
                Sub = $"{d.MediaType} · {DriveCleanupService.FormatBytes(d.TotalBytes)}",
                UsedFraction = d.UsedFraction,
                FreeText = $"{DriveCleanupService.FormatBytes(d.FreeBytes)} free",
            });
        }
    }

    private async Task ScanAsync()
    {
        IsBusy = true;
        StatusLine = "Scanning for reclaimable space…";

        await Task.Run(() => _service.Scan(Items.Select(i => i.Target)));

        _scannedOnce = true;
        foreach (var i in Items) { i.RaiseSize(); i.ResetSelection(); }
        RecalcSelection();

        long total = Items.Sum(i => i.Target.Bytes);
        StatusLine = total == 0
            ? "Nothing to reclaim — your drive is already clean."
            : $"{DriveCleanupService.FormatBytes(total)} reclaimable across {Items.Count(i => i.HasContent)} location(s).";

        IsBusy = false;
    }

    private async Task CleanAsync()
    {
        var chosen = Items.Where(i => i.IsSelected && i.Target.Bytes > 0).ToList();
        if (chosen.Count == 0) return;

        IsBusy = true;
        Progress = 0;
        var progress = new Progress<(double Fraction, string Label)>(p =>
        {
            Progress = p.Fraction;
            StatusLine = $"Cleaning {p.Label}…";
        });

        long freed = await Task.Run(() => _service.Clean(chosen.Select(i => i.Target), progress));

        // Anything that is now empty unticks itself, so the list shows what is left to do.
        foreach (var i in Items) { i.RaiseSize(); i.ResetSelection(); }
        RecalcSelection();
        RefreshDrives();

        var freedText = DriveCleanupService.FormatBytes(freed);
        StatusLine = $"Freed {freedText}.";
        LastCleanupSummary = $"{freedText} freed on {DateTime.Now:d MMM}";
        _main.Home.RefreshSummaries();

        IsBusy = false;
    }

    public void RecalcSelection()
    {
        SelectedBytes = Items.Where(i => i.IsSelected).Sum(i => i.Target.Bytes);
        RelayCommand.RaiseCanExecuteChanged();
    }
}
