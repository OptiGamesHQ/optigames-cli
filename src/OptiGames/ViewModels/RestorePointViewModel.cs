using System.Collections.ObjectModel;
using OptiGames.Core.Services;

namespace OptiGames.ViewModels;

public sealed class RestorePointItemViewModel
{
    public required RestorePoint Point { get; init; }
    public string CreatedAt => Point.CreatedAt == DateTime.MinValue
        ? "Unknown"
        : Point.CreatedAt.ToString("d MMM yyyy, HH:mm");
    public string Description => Point.Description;
    public required RelayCommand RestoreCommand { get; init; }
    public required RelayCommand DeleteCommand { get; init; }
}

public sealed class RestorePointViewModel : PageViewModel
{
    private readonly MainViewModel _main;
    private readonly RestorePointService _service;

    public RestorePointViewModel(MainViewModel main, RestorePointService service)
    {
        _main = main;
        _service = service;
        CreateCommand = new RelayCommand(async () => await CreateAsync(), () => !IsBusy);
    }

    public ObservableCollection<RestorePointItemViewModel> Items { get; } = new();
    public RelayCommand CreateCommand { get; }

    /// <summary>Raw points, newest first. Home reads this for its summary.</summary>
    public IReadOnlyList<RestorePoint> Points { get; private set; } = Array.Empty<RestorePoint>();

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            Raise(nameof(IsIdle));
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsIdle => !IsBusy;

    private string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }

    public bool IsEmpty => Items.Count == 0;

    public override void OnActivated() => Refresh();

    public void Refresh()
    {
        Points = _service.List();

        Items.Clear();
        foreach (var p in Points)
        {
            var captured = p;
            Items.Add(new RestorePointItemViewModel
            {
                Point = captured,
                RestoreCommand = new RelayCommand(async () => await RestoreAsync(captured)),
                DeleteCommand = new RelayCommand(async () => await DeleteAsync(captured)),
            });
        }

        Raise(nameof(IsEmpty));
        Status = Items.Count == 0
            ? "No restore points on this system yet."
            : $"{Items.Count} restore point(s) available.";
    }

    /// <summary>Creates a point. Returns whether it worked so onboarding can gate on it.</summary>
    public async Task<bool> CreateAsync(string? description = null)
    {
        IsBusy = true;
        Status = "Creating restore point — this can take up to a minute…";

        var name = description ?? $"OptiGames — {DateTime.Now:d MMM yyyy HH:mm}";
        bool ok = await Task.Run(() => _service.Create(name));

        Refresh();
        _main.Home.RefreshSummaries();

        Status = ok
            ? "Restore point created."
            : "Could not create a restore point. System Protection may be disabled by policy.";

        IsBusy = false;
        return ok;
    }

    private async Task RestoreAsync(RestorePoint point)
    {
        bool confirmed = await _main.ConfirmAsync(
            "Roll this PC back?",
            $"Windows will restart and restore your system to how it was on {point.CreatedAt:d MMM yyyy 'at' HH:mm}.\n\n" +
            "Save your work first — anything unsaved will be lost.",
            confirmText: "Restart and restore",
            danger: true);

        if (!confirmed) return;

        IsBusy = true;
        Status = "Starting system restore…";
        await Task.Run(() => _service.Restore(point.SequenceNumber));
        IsBusy = false;
    }

    private async Task DeleteAsync(RestorePoint point)
    {
        bool confirmed = await _main.ConfirmAsync(
            "Delete this restore point?",
            "You will no longer be able to roll back to it. Other restore points are unaffected.",
            confirmText: "Delete",
            danger: true);

        if (!confirmed) return;

        IsBusy = true;
        await Task.Run(() => _service.Delete(point.SequenceNumber));
        Refresh();
        _main.Home.RefreshSummaries();
        IsBusy = false;
    }
}
