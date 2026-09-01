using System.Collections.ObjectModel;
using OptiGames.Core.Tweaks;

namespace OptiGames.ViewModels;

/// <summary>
/// One tweak row. The toggle is a staged intent, not a live write — <see cref="IsStaged"/>
/// starts equal to the on-disk state and only differs once the user has changed their mind
/// about something. That difference is what the Optimize button commits.
/// </summary>
/// <summary>One registry value a tweak controls, formatted for the disclosure panel.</summary>
public sealed record TweakChange(string Path, string On, string Off);

public sealed class TweakItemViewModel : ObservableObject
{
    private readonly OptimizeViewModel _owner;

    public Tweak Tweak { get; }

    public TweakItemViewModel(Tweak tweak, OptimizeViewModel owner)
    {
        Tweak = tweak;
        _owner = owner;

        // Built once: the action list is fixed for the life of the catalog.
        Changes = tweak.RelevantActions
            .Select(a => new TweakChange(a.DisplayPath, a.OnText, a.OffText))
            .ToList();

        ToggleChangesCommand = new RelayCommand(() => IsChangesOpen = !IsChangesOpen);
    }

    // ---- Disclosure: exactly what this tweak writes ----

    public IReadOnlyList<TweakChange> Changes { get; }
    public bool HasChanges => Changes.Count > 0;
    public string ChangesLabel => Changes.Count == 1 ? "1 registry value" : $"{Changes.Count} registry values";

    public RelayCommand ToggleChangesCommand { get; }

    private bool _isChangesOpen;
    public bool IsChangesOpen { get => _isChangesOpen; set => Set(ref _isChangesOpen, value); }

    public string Name => Tweak.Name;
    public string Description => Tweak.Description;
    public string? Warning => Tweak.Warning;
    public bool HasWarning => !string.IsNullOrEmpty(Tweak.Warning);
    public bool RequiresReboot => Tweak.RequiresReboot;

    private TweakStatus _status;
    public TweakStatus Status
    {
        get => _status;
        set
        {
            if (!Set(ref _status, value)) return;
            Raise(nameof(StatusText));
            Raise(nameof(IsApplied));
            Raise(nameof(IsPartial));
        }
    }

    public bool IsApplied => Status == TweakStatus.Applied;

    /// <summary>
    /// Some of the tweak's values are set and some are not — usually a half-finished earlier
    /// run, or another tool touching the same keys. The switch renders this as a distinct
    /// mid-position state, because "partly applied" showing an plain off switch is a lie.
    /// </summary>
    public bool IsPartial => Status == TweakStatus.Partial;

    public string StatusText => Status switch
    {
        TweakStatus.Applied => "Applied",
        TweakStatus.Partial => "Partly applied",
        _ => "Not applied",
    };

    private bool _isStaged;
    public bool IsStaged
    {
        get => _isStaged;
        set
        {
            if (!Set(ref _isStaged, value)) return;
            Raise(nameof(IsPending));
            _owner.RecountPending();
        }
    }

    /// <summary>True when the staged intent differs from what is actually on the machine.</summary>
    public bool IsPending => _isStaged != IsApplied;

    /// <summary>Re-reads the machine and resets the toggle to match it.</summary>
    public void SyncFromMachine(TweakEngine engine)
    {
        Status = engine.GetStatus(Tweak);
        _isStaged = IsApplied;
        Raise(nameof(IsStaged));
        Raise(nameof(IsPending));
    }
}

public sealed class TweakGroup
{
    public required string Header { get; init; }
    public required string Blurb { get; init; }
    public required ObservableCollection<TweakItemViewModel> Items { get; init; }
    public bool IsAdvanced { get; init; }
}

public sealed class OptimizeViewModel : PageViewModel
{
    private readonly MainViewModel _main;
    private readonly TweakEngine _engine;
    private readonly List<TweakItemViewModel> _all = new();

    public OptimizeViewModel(MainViewModel main, TweakEngine engine, IReadOnlyList<Tweak> catalog)
    {
        _main = main;
        _engine = engine;

        foreach (var t in catalog)
            _all.Add(new TweakItemViewModel(t, this));

        Groups = new ObservableCollection<TweakGroup>
        {
            new()
            {
                Header = "General",
                Blurb = "Safe, reversible changes. Every one of these can be switched back off from this page.",
                Items = new ObservableCollection<TweakItemViewModel>(
                    _all.Where(i => i.Tweak.Category == TweakCategory.General)),
            },
            new()
            {
                Header = "Advanced",
                Blurb = "Bigger wins, bigger blast radius. Read the warnings before you stage these.",
                IsAdvanced = true,
                Items = new ObservableCollection<TweakItemViewModel>(
                    _all.Where(i => i.Tweak.Category == TweakCategory.Advanced)),
            },
        };

        ApplyCommand = new RelayCommand(async () => await CommitAsync(), () => PendingCount > 0 && !IsBusy);
        SelectRecommendedCommand = new RelayCommand(SelectRecommended, () => !IsBusy);
        ClearCommand = new RelayCommand(ClearStaged, () => !IsBusy);
        UndoCommand = new RelayCommand(async () => await UndoAsync(), () => CanUndo && !IsBusy);
    }

    public ObservableCollection<TweakGroup> Groups { get; }

    public RelayCommand ApplyCommand { get; }
    public RelayCommand SelectRecommendedCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand UndoCommand { get; }

    // ---- Undo ----
    // Reversing a batch by hand means finding every switch you just flipped and flipping it
    // back. The engine is symmetric, so the inverse batch is just the same tweaks with the
    // flag negated — cheap to keep and the single most reassuring control on the page.

    private List<(Tweak Tweak, bool On)> _lastBatch = new();

    private string _undoLabel = "";
    public string UndoLabel { get => _undoLabel; private set => Set(ref _undoLabel, value); }

    private bool _canUndo;
    public bool CanUndo
    {
        get => _canUndo;
        private set { if (Set(ref _canUndo, value)) RelayCommand.RaiseCanExecuteChanged(); }
    }

    public int TotalCount => _all.Count;
    public int AppliedCount => _all.Count(i => i.IsApplied);

    private int _pendingCount;
    public int PendingCount
    {
        get => _pendingCount;
        private set
        {
            if (!Set(ref _pendingCount, value)) return;
            Raise(nameof(ApplyLabel));
            Raise(nameof(HasPending));
        }
    }

    public bool HasPending => PendingCount > 0;

    public string ApplyLabel => PendingCount switch
    {
        0 => "Nothing staged",
        1 => "Apply 1 change",
        _ => $"Apply {PendingCount} changes",
    };

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (Set(ref _isBusy, value)) Raise(nameof(IsIdle)); }
    }

    public bool IsIdle => !IsBusy;

    private double _progress;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    private string _progressLabel = "";
    public string ProgressLabel { get => _progressLabel; private set => Set(ref _progressLabel, value); }

    private bool _rebootPending;
    public bool RebootPending { get => _rebootPending; private set => Set(ref _rebootPending, value); }

    public override void OnActivated() => SyncAll();

    /// <summary>Re-reads every tweak's real state. Cheap — all registry reads.</summary>
    public void SyncAll()
    {
        foreach (var item in _all) item.SyncFromMachine(_engine);
        RecountPending();
        Raise(nameof(AppliedCount));
    }

    public void RecountPending()
    {
        PendingCount = _all.Count(i => i.IsPending);
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void SelectRecommended()
    {
        foreach (var item in _all)
            if (item.Tweak.RecommendedDefault) item.IsStaged = true;
    }

    private void ClearStaged()
    {
        foreach (var item in _all) item.IsStaged = item.IsApplied;
    }

    private async Task CommitAsync()
    {
        var batch = _all.Where(i => i.IsPending)
                        .Select(i => (i.Tweak, On: i.IsStaged))
                        .ToList();
        if (batch.Count == 0) return;

        await RunBatchAsync(batch, isUndo: false);
    }

    /// <summary>Re-runs the previous batch with every flag inverted.</summary>
    private async Task UndoAsync()
    {
        var inverse = _lastBatch.Select(b => (b.Tweak, On: !b.On)).ToList();
        if (inverse.Count == 0) return;

        await RunBatchAsync(inverse, isUndo: true);
    }

    private async Task RunBatchAsync(List<(Tweak Tweak, bool On)> batch, bool isUndo)
    {
        IsBusy = true;
        Progress = 0;
        _main.Log($"Applying {batch.Count} change(s).");

        var progress = new Progress<(double Fraction, string Label)>(p =>
        {
            Progress = p.Fraction;
            ProgressLabel = p.Label;
        });

        var results = await Task.Run(() => _engine.RunBatch(batch, progress));

        var failed = results.Where(r => !r.Success).ToList();
        if (failed.Count > 0)
            _main.Log($"{failed.Count} change(s) failed: {string.Join(", ", failed.Select(f => f.Tweak.Name))}");

        RebootPending = results.Any(r => r.Success && r.Tweak.RequiresReboot);

        SyncAll();
        Raise(nameof(AppliedCount));
        _main.Home.RefreshSummaries();

        // Undoing an undo would just re-apply what you asked to reverse, so the offer is
        // withdrawn once taken. Only tweaks that actually succeeded are worth reversing.
        var reversible = results.Where(r => r.Success)
                                .Select(r => batch.First(b => b.Tweak == r.Tweak))
                                .ToList();

        if (isUndo || reversible.Count == 0)
        {
            _lastBatch = new List<(Tweak, bool)>();
            CanUndo = false;
        }
        else
        {
            _lastBatch = reversible;
            UndoLabel = reversible.Count == 1
                ? "Applied 1 change"
                : $"Applied {reversible.Count} changes";
            CanUndo = true;
        }

        ProgressLabel = failed.Count == 0
            ? (isUndo ? "Changes reversed" : "All changes applied")
            : "Finished with errors";

        IsBusy = false;
    }
}
