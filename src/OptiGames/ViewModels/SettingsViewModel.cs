using System.Diagnostics;
using System.Reflection;
using OptiGames.Core.Services;

namespace OptiGames.ViewModels;

public sealed record HelpLink(string Icon, string Title, string Blurb, string Url, bool Accent = false);

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
    public HelpViewModel()
    {
        OpenCommand = new RelayCommand(p =>
        {
            if (p is string url) SettingsViewModel.OpenPath(url);
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
                     "https://optigames.gg/discord"),
        new HelpLink("I.Bug", "Report a bug",
                     "Found something broken? Tell us what happened and we will fix it.",
                     "https://optigames.gg/report", Accent: true),
        new HelpLink("I.Globe", "Visit the website",
                     "Guides, changelogs and the latest build.",
                     "https://optigames.gg"),
    };
}
