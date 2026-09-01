using System.Diagnostics;
using System.Reflection;
using OptiGames.Core.Services;

namespace OptiGames.ViewModels;

/// <summary>
/// A row on the Help page. <paramref name="Filled"/> marks an icon that is a solid logo rather
/// than one of the stroked glyphs in Icons.xaml, so the view fills it instead of stroking it —
/// a brand mark drawn as an outline reads as the wrong logo, not as a house style.
/// </summary>
public sealed record HelpLink(
    string Icon,
    string Title,
    string Blurb,
    string Url,
    bool Accent = false,
    bool Filled = false,
    /// <summary>Opens the in-app report form instead of navigating to <paramref name="Url"/>.</summary>
    bool OpensReportForm = false);

public sealed class SettingsViewModel : PageViewModel
{
    private readonly MainViewModel _main;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        OpenToolFolderCommand = new RelayCommand(() => OpenPath(AppPaths.Ensure()));
        ReplayOnboardingCommand = new RelayCommand(() => _main.StartOnboarding(force: true));
        ClearLogCommand = new RelayCommand(_main.ClearLog);
    }

    public RelayCommand OpenToolFolderCommand { get; }
    public RelayCommand ReplayOnboardingCommand { get; }
    public RelayCommand ClearLogCommand { get; }

    public string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public string ToolFolder => AppPaths.Root;

    /// <summary>Mirrors <see cref="MainViewModel.ShowAdvanced"/> so the switch can live here too.</summary>
    public bool ShowAdvanced
    {
        get => _main.ShowAdvanced;
        set { _main.ShowAdvanced = value; Raise(); }
    }

    public bool ShowTips
    {
        get => _main.ShowTips;
        set { _main.ShowTips = value; Raise(); }
    }

    internal static void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* no shell association — nothing useful to show the user */ }
    }
}

public sealed class HelpViewModel : PageViewModel
{
    private readonly MainViewModel _main;

    public HelpViewModel(MainViewModel main)
    {
        _main = main;

        // One command for every row. A link opens its URL; the bug row opens the in-app form,
        // because a report filed from here can carry the hardware, the applied tweaks and the
        // action log, none of which a browser form could ask the user to supply.
        OpenCommand = new RelayCommand(p =>
        {
            if (p is not HelpLink link) return;
            if (link.OpensReportForm) _main.OpenBugReport();
            else SettingsViewModel.OpenPath(link.Url);
        });
    }

    public RelayCommand OpenCommand { get; }

    public IReadOnlyList<HelpLink> Links { get; } = new[]
    {
        new HelpLink("I.Mail", "Need help?",
                     "Reach the support team by email and we will get back to you.",
                     "mailto:support@optigames.gg"),
        new HelpLink("I.Discord", "Join the Discord",
                     "Talk to the team and other players, and hear about updates first.",
                     "https://discord.com/invite/FeagtcsxXm", Filled: true),
        new HelpLink("I.Bug", "Report a bug",
                     "Something broken? Send it with screenshots and your system details.",
                     "", Accent: true, OpensReportForm: true),
        new HelpLink("I.Globe", "Visit the website",
                     "Guides, changelogs and the latest build.",
                     "https://optigames.gg"),
    };
}
