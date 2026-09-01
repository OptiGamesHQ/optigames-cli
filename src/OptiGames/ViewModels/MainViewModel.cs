using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using OptiGames.Core;
using OptiGames.Core.Helpers;
using OptiGames.Core.Services;
using OptiGames.Core.Tweaks;

namespace OptiGames.ViewModels;

/// <summary>An in-app modal. Replaces MessageBox so confirmations match the rest of the UI.</summary>
public sealed class ModalViewModel : ObservableObject
{
    private readonly TaskCompletionSource<bool> _completion = new();

    public required string Title { get; init; }
    public required string Body { get; init; }
    public string ConfirmText { get; init; } = "OK";
    public string CancelText { get; init; } = "Cancel";
    public bool ShowCancel { get; init; } = true;
    public bool Danger { get; init; }

    public Task<bool> Result => _completion.Task;

    public RelayCommand ConfirmCommand => new(() => _completion.TrySetResult(true));
    public RelayCommand CancelCommand => new(() => _completion.TrySetResult(false));
}

public sealed class MainViewModel : ObservableObject
{
    private readonly ILogSink _log;
    private readonly StringBuilder _logBuffer = new();

    public MainViewModel()
    {
        _log = new DelegateLogSink(AppendLog);

        var ctx = new TweakContext(_log);
        var engine = new TweakEngine(ctx);
        var catalog = TweakCatalog.Build(ctx);

        var restoreService = new RestorePointService(ctx.Process, ctx.Registry, _log);
        var cleanupService = new DriveCleanupService(_log);

        Home = new HomeViewModel(this) { Title = "Home", Icon = "I.Home" };
        Optimize = new OptimizeViewModel(this, engine, catalog) { Title = "Optimize", Icon = "I.Bolt" };
        Clean = new CleanDriveViewModel(this, cleanupService) { Title = "Clean Drive", Icon = "I.Disk" };
        Restore = new RestorePointViewModel(this, restoreService) { Title = "Restore Point", Icon = "I.Shield" };
        Help = new HelpViewModel { Title = "Help", Icon = "I.Help", IsFooter = true };
        Settings = new SettingsViewModel(this) { Title = "Settings", Icon = "I.Settings", IsFooter = true };

        Pages = new ObservableCollection<PageViewModel> { Home, Optimize, Clean, Restore, Help, Settings };
        MainPages = new ObservableCollection<PageViewModel>(Pages.Where(p => !p.IsFooter));
        FooterPages = new ObservableCollection<PageViewModel>(Pages.Where(p => p.IsFooter));

        Onboarding = new OnboardingViewModel(this);

        // Both read once at startup: Home summarises the tweak count and the newest restore
        // point, and would otherwise report zero until you visit those pages yourself.
        Restore.Refresh();
        Optimize.SyncAll();

        _current = Home;
        Home.IsSelected = true;
        Home.OnActivated();
    }

    // ---------------------------------------------------------------- navigation

    public ObservableCollection<PageViewModel> Pages { get; }
    public ObservableCollection<PageViewModel> MainPages { get; }
    public ObservableCollection<PageViewModel> FooterPages { get; }

    public HomeViewModel Home { get; }
    public OptimizeViewModel Optimize { get; }
    public CleanDriveViewModel Clean { get; }
    public RestorePointViewModel Restore { get; }
    public HelpViewModel Help { get; }
    public SettingsViewModel Settings { get; }
    public OnboardingViewModel Onboarding { get; }

    private PageViewModel _current;
    public PageViewModel Current
    {
        get => _current;
        set
        {
            if (value is null || !Set(ref _current, value)) return;
            foreach (var page in Pages) page.IsSelected = ReferenceEquals(page, value);
            value.OnActivated();
            MaybeShowTip(value);
        }
    }

    public RelayCommand NavigateCommand => _navigate ??=
        new RelayCommand(p => { if (p is PageViewModel page) Current = page; });
    private RelayCommand? _navigate;

    public void Navigate<T>() where T : PageViewModel
        => Current = Pages.OfType<T>().First();

    // ---------------------------------------------------------------- preferences

    private bool _showAdvanced = true;
    public bool ShowAdvanced { get => _showAdvanced; set => Set(ref _showAdvanced, value); }

    private bool _showTips = true;
    public bool ShowTips { get => _showTips; set => Set(ref _showTips, value); }

    // ---------------------------------------------------------------- modal

    private ModalViewModel? _modal;
    public ModalViewModel? Modal
    {
        get => _modal;
        private set { if (Set(ref _modal, value)) Raise(nameof(IsModalOpen)); }
    }

    public bool IsModalOpen => Modal is not null;

    /// <summary>Shows a two-button modal and completes when the user picks one.</summary>
    public async Task<bool> ConfirmAsync(string title, string body,
                                         string confirmText = "Confirm", bool danger = false)
    {
        var modal = new ModalViewModel
        {
            Title = title, Body = body, ConfirmText = confirmText, Danger = danger,
        };
        Modal = modal;
        bool result = await modal.Result;
        Modal = null;
        return result;
    }

    /// <summary>Shows a single-button modal — used by the per-page tour.</summary>
    public async Task NoticeAsync(string title, string body, string confirmText = "Got it")
    {
        var modal = new ModalViewModel
        {
            Title = title, Body = body, ConfirmText = confirmText, ShowCancel = false,
        };
        Modal = modal;
        await modal.Result;
        Modal = null;
    }

    // ---------------------------------------------------------------- page tour

    private readonly HashSet<string> _tipsSeen = new();

    private static readonly Dictionary<string, string> Tips = new()
    {
        ["Optimize"] =
            "Flipping a switch here does not change anything yet — it stages the change. " +
            "Review what you have picked, then press Apply at the bottom to commit the whole " +
            "batch at once. Switching something back off and applying reverts it cleanly.",
        ["Clean Drive"] =
            "Each row is a cache Windows never clears on its own, sized so you can see what " +
            "you get back. Nothing here touches your documents, downloads or game installs.",
        ["Restore Point"] =
            "A restore point is a full snapshot of your system settings. If a tweak ever " +
            "causes trouble, roll back here and Windows undoes everything in one step.",
    };

    private void MaybeShowTip(PageViewModel page)
    {
        if (!ShowTips) return;
        if (!Tips.TryGetValue(page.Title, out var body)) return;
        if (!_tipsSeen.Add(page.Title)) return;

        _ = NoticeAsync($"This is the {page.Title} page", body);
    }

    /// <summary>Lets Settings replay the tour.</summary>
    public void ResetTips() => _tipsSeen.Clear();

    // ---------------------------------------------------------------- onboarding

    private bool _isOnboarding;
    public bool IsOnboarding { get => _isOnboarding; private set => Set(ref _isOnboarding, value); }

    public void StartOnboardingIfFirstRun()
    {
        if (AppPaths.HasOnboarded) return;
        StartOnboarding(force: false);
    }

    public void StartOnboarding(bool force)
    {
        if (force) ResetTips();
        Onboarding.Begin();
        IsOnboarding = true;
    }

    public void FinishOnboarding()
    {
        AppPaths.MarkOnboarded();
        IsOnboarding = false;
        Current = Home;
        Home.RefreshSummaries();
    }

    // ---------------------------------------------------------------- log

    private string _logText = "";
    public string LogText { get => _logText; private set => Set(ref _logText, value); }

    public void Log(string message) => AppendLog(message);

    public void ClearLog()
    {
        lock (_logBuffer) _logBuffer.Clear();
        LogText = "";
    }

    /// <summary>
    /// Tweaks run on background threads, so every append hops to the dispatcher. The buffer
    /// is capped because a full browser-debloat run emits a few hundred lines.
    /// </summary>
    private void AppendLog(string message)
    {
        string snapshot;
        lock (_logBuffer)
        {
            _logBuffer.AppendLine(message);
            if (_logBuffer.Length > 200_000)
                _logBuffer.Remove(0, _logBuffer.Length - 150_000);
            snapshot = _logBuffer.ToString();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) LogText = snapshot;
        else dispatcher.BeginInvoke(() => LogText = snapshot);
    }
}
