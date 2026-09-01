using OptiGames.Core.Services;

namespace OptiGames.ViewModels;

public enum OnboardingStep
{
    Welcome,
    Hardware,
    RestorePoint,
    Ready,
}

/// <summary>
/// First-run flow. The restore-point step is a real gate: you cannot reach the app until a
/// checkpoint exists, because every other page can change the machine. The one exception is
/// a machine where System Protection is off by policy — there we unlock with a warning
/// rather than trapping the user on a step they cannot complete.
/// </summary>
public sealed class OnboardingViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly SystemInfoProvider _info = new();

    public OnboardingViewModel(MainViewModel main)
    {
        _main = main;
        NextCommand = new RelayCommand(Next, () => CanAdvance);
        CreateRestorePointCommand = new RelayCommand(async () => await CreateAsync(), () => !IsWorking);
    }

    public RelayCommand NextCommand { get; }
    public RelayCommand CreateRestorePointCommand { get; }

    private OnboardingStep _step = OnboardingStep.Welcome;
    public OnboardingStep Step
    {
        get => _step;
        private set
        {
            if (!Set(ref _step, value)) return;
            foreach (var n in new[]
            {
                nameof(IsWelcome), nameof(IsHardware), nameof(IsRestore), nameof(IsReady),
                nameof(CanAdvance), nameof(NextLabel), nameof(StepIndex),
            }) Raise(n);
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsWelcome => Step == OnboardingStep.Welcome;
    public bool IsHardware => Step == OnboardingStep.Hardware;
    public bool IsRestore => Step == OnboardingStep.RestorePoint;
    public bool IsReady => Step == OnboardingStep.Ready;

    /// <summary>1-based, for the "Step 2 of 4" readout and the progress dots.</summary>
    public int StepIndex => (int)Step + 1;
    public int StepCount => 4;

    public IReadOnlyList<SpecLine> Specs { get; private set; } = Array.Empty<SpecLine>();

    private bool _isWorking;
    public bool IsWorking
    {
        get => _isWorking;
        private set
        {
            if (!Set(ref _isWorking, value)) return;
            Raise(nameof(CanAdvance));
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    private bool _restorePointReady;
    public bool RestorePointReady
    {
        get => _restorePointReady;
        private set
        {
            if (!Set(ref _restorePointReady, value)) return;
            Raise(nameof(CanAdvance));
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    private string _restoreStatus = "";
    public string RestoreStatus { get => _restoreStatus; private set => Set(ref _restoreStatus, value); }

    private bool _restoreFailed;
    public bool RestoreFailed { get => _restoreFailed; private set => Set(ref _restoreFailed, value); }

    /// <summary>The restore step is the only one that blocks; the rest advance freely.</summary>
    public bool CanAdvance => Step != OnboardingStep.RestorePoint || (RestorePointReady && !IsWorking);

    public string NextLabel => Step switch
    {
        OnboardingStep.Welcome => "Get started",
        OnboardingStep.Hardware => "Looks right",
        OnboardingStep.RestorePoint => "Continue",
        _ => "Open OptiGames",
    };

    /// <summary>Loads hardware and pre-checks whether a usable restore point already exists.</summary>
    public void Begin()
    {
        Step = OnboardingStep.Welcome;
        RestoreFailed = false;

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

        // A point made in the last day is recent enough to count as the safety net.
        var existing = _main.Restore.Points.FirstOrDefault();
        if (existing is not null && (DateTime.Now - existing.CreatedAt).TotalHours < 24)
        {
            RestorePointReady = true;
            RestoreStatus = $"You already have a restore point from {existing.CreatedAt:HH:mm} today.";
        }
        else
        {
            RestorePointReady = false;
            RestoreStatus = "";
        }
    }

    private void Next()
    {
        if (Step == OnboardingStep.Ready)
        {
            _main.FinishOnboarding();
            return;
        }
        Step = (OnboardingStep)((int)Step + 1);
    }

    private async Task CreateAsync()
    {
        IsWorking = true;
        RestoreFailed = false;
        RestoreStatus = "Creating your first restore point…";

        bool ok = await _main.Restore.CreateAsync("OptiGames — before any changes");

        if (ok)
        {
            RestorePointReady = true;
            RestoreStatus = "Restore point created. Your system is snapshotted.";
        }
        else
        {
            // Do not trap the user on a step their machine will not let them finish.
            RestoreFailed = true;
            RestorePointReady = true;
            RestoreStatus = "System Protection is turned off by policy on this PC, so no restore " +
                            "point could be made. You can continue, but roll-back will only be " +
                            "per-tweak from the Optimize page.";
        }

        IsWorking = false;
    }
}
