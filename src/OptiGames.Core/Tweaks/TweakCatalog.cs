using Microsoft.Win32;
using OptiGames.Core.Services;

namespace OptiGames.Core.Tweaks;

/// <summary>
/// The full catalog. Every tweak carries both an on-state and an authored off-state, so a
/// revert restores the Windows default rather than whatever happened to be there first.
/// Where Windows ships no value at all, the off-state deletes it.
/// </summary>
public static class TweakCatalog
{
    private const RegistryHive HKCU = RegistryHive.CurrentUser;
    private const RegistryHive HKLM = RegistryHive.LocalMachine;

    public static IReadOnlyList<Tweak> Build(TweakContext ctx) => new List<Tweak>
    {
        GameBar(),
        StoreAutoUpdate(),
        DisplayPerformance(),
        GameMode(),
        Telemetry(),
        PowerPlan(),
        NvidiaProfile(ctx),

        BrowserDebloat(),
        FullscreenOptimizations(),
        NotificationCenter(),
        Win10ContextMenu(),
        BackgroundApps(),
        Hibernation(),
        StorageSense(),
        Hags(),
        WindowsUpdatePause(),
        VirtualizationBasedSecurity(),
    }.Where(t => t.Supported).ToList();

    // ------------------------------------------------------------------ General

    private static Tweak GameBar() => new()
    {
        Id = "game-bar",
        Name = "Disable Xbox Game Bar",
        Description = "Turns off the Game Bar overlay and Game DVR background recording, " +
                      "which hook every fullscreen app and cost frames even when idle.",
        RecommendedDefault = true,
        Actions = new[]
        {
            RegAction.Dword(HKLM, @"Software\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", on: 0, off: null),
            RegAction.Dword(HKCU, @"Software\Microsoft\GameBar", "ShowStartupPanel", on: 0, off: 1),
            RegAction.Dword(HKCU, @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", on: 0, off: 1),
            RegAction.Dword(HKCU, @"Software\Microsoft\GameBar", "GamepadNexusChordEnabled", on: 0, off: 1),
        },
    };

    private static Tweak StoreAutoUpdate() => new()
    {
        Id = "store-autoupdate",
        Name = "Disable Store Auto-Update",
        Description = "Stops the Microsoft Store downloading app updates in the background " +
                      "while you are playing. You can still update manually.",
        RecommendedDefault = true,
        Actions = new[]
        {
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\MicrosoftStore\AutoDownload",
                            "AutoDownload", on: 0, off: null),
        },
    };

    private static Tweak DisplayPerformance() => new()
    {
        Id = "display-performance",
        Name = "Display Performance",
        Description = "Strips animation, transparency and input delay out of the Windows shell: " +
                      "instant menus, no window-drag ghosting, no taskbar clutter.",
        RecommendedDefault = true,
        Actions = new[]
        {
            // Visuals
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", on: 0, off: 1),
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", on: 3, off: 0),

            // Taskbar
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl", on: 0, off: 1),
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarSn", on: 0, off: null),
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarSd", on: 0, off: 1),
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarMn", on: 0, off: 1),
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", on: 0, off: 1),
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", on: 0, off: 1),
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowTaskViewButton", on: 0, off: 1),
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", on: 0, off: 1),
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCortanaButton", on: 0, off: 1),
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Feeds", "ShellFeedsTaskbarViewMode", on: 2, off: null),
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", on: 0, off: null),

            // Explorer list rendering
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly", on: 0, off: 0),
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ListviewAlphaSelect", on: 0, off: 1),
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ListviewShadow", on: 0, off: 1),

            // Desktop Window Manager
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\DWM", "EnableAeroPeek", on: 0, off: 1),
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\DWM", "AlwaysHibernateThumbnails", on: 0, off: null),

            // Input responsiveness
            RegAction.Str(HKCU, @"Control Panel\Mouse", "MouseHoverTime", on: "10", off: "400"),
            RegAction.Dword(HKCU, @"Control Panel\Keyboard", "KeyboardDelay", on: 0, off: 1),
            RegAction.Str(HKCU, @"Control Panel\Desktop", "MenuShowDelay", on: "0", off: "400"),
            RegAction.Str(HKCU, @"Control Panel\Desktop", "DragFullWindows", on: "0", off: "1"),
            RegAction.Str(HKCU, @"Control Panel\Desktop", "FontSmoothing", on: "2", off: "2"),
            RegAction.Binary(HKCU, @"Control Panel\Desktop", "UserPreferencesMask",
                             onHex: "9012038010000000", offHex: "9E3E078012000000"),
            RegAction.Str(HKCU, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", on: "0", off: "1"),
        },
        CustomApply = _ => ShellRefresh.Broadcast(),
        CustomRevert = _ => ShellRefresh.Broadcast(),
    };

    private static Tweak GameMode() => new()
    {
        Id = "game-mode",
        Name = "Enable Windows Game Mode",
        Description = "Lets Windows prioritise the foreground game for CPU and GPU time and " +
                      "hold back background work while you play.",
        RecommendedDefault = true,
        Actions = new[]
        {
            RegAction.Dword(HKCU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", on: 1, off: 0),
        },
    };

    private static Tweak Telemetry() => new()
    {
        Id = "telemetry",
        Name = "Disable Microsoft Telemetry",
        Description = "Stops Windows uploading diagnostic and usage data about your machine.",
        RecommendedDefault = true,
        Actions = new[]
        {
            RegAction.Dword(HKCU, @"Software\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", on: 0, off: null),
        },
    };

    private static Tweak PowerPlan() => new()
    {
        Id = "power-plan",
        Name = "High-Performance Power Plan",
        Description = "Imports an OptiGames power plan and switches to it, so the CPU holds its " +
                      "top clock and Windows stops parking cores or idling PCIe and USB devices. " +
                      "Reverting puts you back on the plan you were using and deletes the plan again.",
        RecommendedDefault = true,
        // Hidden if the plan file was not compiled into the build.
        IsSupported = () => PowerPlanService.HasPayload,
        // powercfg owns the plan, so there is nothing in the registry to read — without
        // CustomStatus the engine would report this as never applied.
        CustomStatus = c => new PowerPlanService(c).IsApplied ? TweakStatus.Applied : TweakStatus.NotApplied,
        CustomApply = c => new PowerPlanService(c).Apply(),
        CustomRevert = c => new PowerPlanService(c).Revert(),
    };

    private static Tweak NvidiaProfile(TweakContext ctx) => new()
    {
        Id = "nvidia-profile",
        Name = "Custom NVIDIA Profile",
        Description = "Applies a tuned global driver profile — low-latency mode, maximum " +
                      "performance power state, and gaming-first texture filtering.",
        RequiresReboot = false,
        Warning = "Reverting restarts the NVIDIA display service, which blanks the screen for a second.",
        // Hidden until both an NVIDIA GPU and the profile payload are present.
        IsSupported = () => Hardware.HasNvidiaGpu && NvidiaProfileService.HasProfilePayload,
        // The driver keeps profiles in its own database, not the registry, so status comes from
        // a marker file; with no actions and no CustomStatus the engine would say NotApplied.
        CustomStatus = c => new NvidiaProfileService(c).IsApplied ? TweakStatus.Applied : TweakStatus.NotApplied,
        CustomApply = c => new NvidiaProfileService(c).Apply(),
        CustomRevert = c => new NvidiaProfileService(c).Revert(),
    };

    // ----------------------------------------------------------------- Advanced

    private static Tweak BrowserDebloat() => new()
    {
        Id = "browser-debloat",
        Name = "Debloat Installed Browsers",
        Description = "Detects Brave, Chrome and Edge and switches off their background " +
                      "processes, promos, AI features and telemetry via enterprise policy.",
        Category = TweakCategory.Advanced,
        Actions = BrowserActions(),
    };

    private static RegAction[] BrowserActions()
    {
        var list = new List<RegAction>();

        // ---- Brave, only when it is installed ----
        const string brave = @"SOFTWARE\Policies\BraveSoftware\Brave";
        foreach (var (name, on) in new (string, int)[]
        {
            ("PromptForDownloadLocation", 0), ("BraveRewardsDisabled", 1), ("BraveWalletDisabled", 1),
            ("BraveVPNDisabled", 1), ("BraveNewsDisabled", 1), ("BraveTalkDisabled", 1),
            ("BravePlaylistEnabled", 0), ("BraveAIChatEnabled", 0), ("BackgroundModeEnabled", 0),
            ("PromotionsEnabled", 0), ("AIModeSettings", 1), ("WebRtcEventLogCollectionAllowed", 0),
            ("UrlKeyedAnonymizedDataCollectionEnabled", 0), ("SearchContentSharingSettings", 1),
            ("MetricsReportingEnabled", 0), ("FeedbackSurveysEnabled", 0),
        })
        {
            list.Add(new RegAction(HKLM, brave, name, RegState.Dword(on), RegState.Delete)
            {
                AppliesWhen = () => Browsers.BraveInstalled,
            });
        }

        // ---- Chrome, only when it is installed ----
        const string chrome = @"SOFTWARE\Policies\Google\Chrome";
        foreach (var (name, on) in new (string, int)[]
        {
            ("BackgroundModeEnabled", 0), ("PromotionsEnabled", 0), ("HighEfficiencyModeEnabled", 1),
            ("AIModeSettings", 1), ("DefaultNotificationsSetting", 2),
            ("GenAILocalFoundationalModelSettings", 1), ("ChromeSuggestionsSettings", 1),
            ("PromptForDownloadLocation", 0),
        })
        {
            list.Add(new RegAction(HKLM, chrome, name, RegState.Dword(on), RegState.Delete)
            {
                AppliesWhen = () => Browsers.ChromeInstalled,
            });
        }

        // ---- Edge ships with Windows, so these always apply ----
        const string legacyEdge = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Storage\microsoft.microsoftedge_8wekyb3d8bbwe\MicrosoftEdge";
        list.Add(RegAction.Dword(HKCU, legacyEdge + @"\Main", "DoNotTrack", on: 1, off: null));
        list.Add(RegAction.Dword(HKCU, legacyEdge + @"\Main", "ShowSearchSuggestionsGlobal", on: 0, off: null));
        list.Add(RegAction.Dword(HKCU, legacyEdge + @"\ServiceUI", "EnableCortana", on: 0, off: null));
        list.Add(RegAction.Dword(HKCU, legacyEdge + @"\FlipAhead", "FPEnabled", on: 0, off: null));
        list.Add(RegAction.Dword(HKCU, legacyEdge + @"\ServiceUI\ShowSearchHistory", "", on: 0, off: null));

        list.Add(RegAction.Dword(HKLM, @"Software\Policies\Microsoft\MicrosoftEdge\Main", "TabPreloader", on: 0, off: null));
        list.Add(RegAction.Dword(HKLM, @"Software\Policies\Microsoft\MicrosoftEdge\TabPreloader", "AllowTabPreloading", on: 0, off: null));

        const string edge = @"Software\Policies\Microsoft\Edge";
        foreach (var (name, on) in new (string, int)[]
        {
            ("StartupBoostEnabled", 0), ("BackgroundModeEnabled", 0), ("PersonalizationReportingEnabled", 0),
            ("ShowRecommendationsEnabled", 0), ("HideFirstRunExperience", 1), ("ConfigureDoNotTrack", 1),
            ("AlternateErrorPagesEnabled", 0), ("EdgeCollectionsEnabled", 0), ("EdgeShoppingAssistantEnabled", 0),
            ("MicrosoftEdgeInsiderPromotionEnabled", 0), ("ShowMicrosoftRewards", 0), ("WebWidgetAllowed", 0),
            ("DiagnosticData", 0), ("EdgeAssetDeliveryServiceEnabled", 0), ("WalletDonationEnabled", 0),
            ("DefaultBrowserSettingsCampaignEnabled", 0),
        })
        {
            list.Add(RegAction.Dword(HKLM, edge, name, on, off: null));
        }

        list.Add(RegAction.Dword(HKLM, @"Software\Policies\Microsoft\EdgeUpdate", "CreateDesktopShortcutDefault", on: 0, off: null));

        return list.ToArray();
    }

    private static Tweak FullscreenOptimizations() => new()
    {
        Id = "fullscreen-optimizations",
        Name = "Disable Fullscreen Optimizations",
        Description = "Forces true exclusive fullscreen instead of the borderless-window " +
                      "compositor path, cutting a frame of present latency in most games.",
        Category = TweakCategory.Advanced,
        Actions = new[]
        {
            RegAction.Dword(HKCU, @"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", on: 1, off: 0),
        },
    };

    private static Tweak NotificationCenter() => new()
    {
        Id = "notification-center",
        Name = "Disable Notification Tray & Calendar",
        Description = "Removes the Action Center flyout so notifications cannot steal focus " +
                      "mid-game.",
        Category = TweakCategory.Advanced,
        Actions = new[]
        {
            RegAction.Dword(HKCU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableNotificationCenter", on: 1, off: null),
        },
        CustomApply = _ => ShellRefresh.RestartExplorer(),
        CustomRevert = _ => ShellRefresh.RestartExplorer(),
    };

    private static Tweak Win10ContextMenu() => new()
    {
        Id = "win10-context-menu",
        Name = "Windows 10 Right-Click Menu",
        Description = "Restores the full classic context menu, removing the extra " +
                      "\"Show more options\" click Windows 11 added.",
        Category = TweakCategory.Advanced,
        Actions = new[]
        {
            RegAction.DefaultValueShim(HKCU,
                @"SOFTWARE\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32"),
        },
        CustomApply = _ => ShellRefresh.RestartExplorer(),
        CustomRevert = _ => ShellRefresh.RestartExplorer(),
    };

    private static Tweak BackgroundApps() => new()
    {
        Id = "background-apps",
        Name = "Disable Background Apps",
        Description = "Blocks Store apps from running while you are not using them.",
        Category = TweakCategory.Advanced,
        Actions = new[]
        {
            RegAction.Dword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", on: 2, off: null),
        },
    };

    private static Tweak Hibernation() => new()
    {
        Id = "hibernation",
        Name = "Disable Hibernation",
        Description = "Turns off hibernate and reclaims the hiberfil.sys file, which is " +
                      "sized at a large fraction of your installed RAM.",
        Category = TweakCategory.Advanced,
        Actions = new[]
        {
            RegAction.Dword(HKLM, @"Software\Microsoft\Windows\CurrentVersion\Explorer\FlyoutMenuSettings", "ShowHibernateOption", on: 0, off: 1),
            // CurrentControlSet, not ControlSet001 — the numbered set is not always the live one.
            RegAction.Dword(HKLM, @"System\CurrentControlSet\Control\Session Manager\Power", "HibernateEnabled", on: 0, off: 1),
        },
        // The registry flag alone does not delete hiberfil.sys; powercfg does.
        CustomApply = c => c.Process.Run("powercfg.exe", "/hibernate off"),
        CustomRevert = c => c.Process.Run("powercfg.exe", "/hibernate on"),
    };

    private static Tweak StorageSense() => new()
    {
        Id = "storage-sense",
        Name = "Disable Storage Sense",
        Description = "Stops Windows automatically deleting files it decides are unused. " +
                      "Use the Clean Drive page instead, where you choose what goes.",
        Category = TweakCategory.Advanced,
        Actions = new[]
        {
            RegAction.Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", on: 0, off: 1),
        },
    };

    private static Tweak Hags() => new()
    {
        Id = "hags",
        Name = "Hardware-Accelerated GPU Scheduling",
        Description = "Hands frame scheduling to the GPU instead of the CPU driver thread. " +
                      "Reduces latency on modern GPUs; older cards can lose stability.",
        Category = TweakCategory.Advanced,
        RequiresReboot = true,
        Actions = new[]
        {
            // 2 = enabled, 1 = disabled. This turns HAGS ON.
            RegAction.Dword(HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", on: 2, off: 1),
        },
    };

    private static Tweak WindowsUpdatePause() => new()
    {
        Id = "windows-update-pause",
        Name = "Pause Windows Updates",
        Description = "Pushes the update pause window out to the year 3000 so Windows never " +
                      "downloads or installs an update on its own.",
        Category = TweakCategory.Advanced,
        Warning = "You will stop receiving security patches until this is reverted.",
        Actions = new[]
        {
            RegAction.Dword(HKLM, @"SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\Settings", "PausedFeatureStatus", on: 0, off: null),
            RegAction.Dword(HKLM, @"SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\Settings", "PausedQualityStatus", on: 0, off: null),
            RegAction.Dword(HKLM, @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "FlightSettingsMaxPauseDays", on: 0x0e42, off: null),

            RegAction.Timestamp(HKLM, @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "PauseFeatureUpdatesStartTime", Today),
            RegAction.Str(HKLM, @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "PauseFeatureUpdatesEndTime", on: Forever, off: null),
            RegAction.Timestamp(HKLM, @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "PauseQualityUpdatesStartTime", Today),
            RegAction.Str(HKLM, @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "PauseQualityUpdatesEndTime", on: Forever, off: null),
            RegAction.Timestamp(HKLM, @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "PauseUpdatesStartTime", Today),
            RegAction.Str(HKLM, @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "PauseUpdatesExpiryTime", on: Forever, off: null),
        },
    };

    private static Tweak VirtualizationBasedSecurity() => new()
    {
        Id = "vbs",
        Name = "Disable Virtualization-Based Security",
        Description = "Turns off VBS and Memory Integrity (HVCI). These run your kernel " +
                      "inside a hypervisor and cost 5–15% CPU in games.",
        Category = TweakCategory.Advanced,
        RequiresReboot = true,
        Warning = "Removes a kernel-level exploit defence. Some anti-cheats require VBS.",
        Actions = new[]
        {
            RegAction.Dword(HKLM, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", on: 0, off: 1),
            RegAction.Dword(HKLM, @"SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity", on: 0, off: 1),
        },
    };

    // ------------------------------------------------------------------ Helpers

    private const string Forever = "3000-11-06T14:03:37Z";
    private static string Today() => DateTime.UtcNow.ToString("yyyy-MM-dd") + "T00:00:00Z";
}
