namespace OptiGames.Core.Tweaks;

public enum TweakCategory
{
    General,
    Advanced,
}

public enum TweakStatus
{
    /// <summary>None of the tweak's values are in their applied state.</summary>
    NotApplied,
    /// <summary>Every value is in its applied state.</summary>
    Applied,
    /// <summary>Some values are applied and some are not — usually a partial earlier run.</summary>
    Partial,
}

/// <summary>
/// One reversible change. Almost every tweak is fully described by its registry actions;
/// the two that are not (NVIDIA profile, anything needing a helper binary) supply
/// <see cref="CustomApply"/> / <see cref="CustomRevert"/> instead.
/// </summary>
public sealed class Tweak
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public TweakCategory Category { get; init; } = TweakCategory.General;

    /// <summary>Shown as a red warning badge; set for anything that weakens security.</summary>
    public string? Warning { get; init; }

    /// <summary>Surfaces a "restart required" hint once the tweak is applied.</summary>
    public bool RequiresReboot { get; init; }

    /// <summary>Recommended tweaks are pre-selected when the Optimize page first loads.</summary>
    public bool RecommendedDefault { get; init; }

    public IReadOnlyList<RegAction> Actions { get; init; } = Array.Empty<RegAction>();

    /// <summary>Runs after the registry actions when the tweak is switched on.</summary>
    public Action<TweakContext>? CustomApply { get; init; }

    /// <summary>Runs after the registry actions when the tweak is switched off.</summary>
    public Action<TweakContext>? CustomRevert { get; init; }

    /// <summary>
    /// Overrides status detection for tweaks whose applied state is not visible in the
    /// registry (the NVIDIA profile lives in the driver database, not HKLM).
    /// </summary>
    public Func<TweakContext, TweakStatus>? CustomStatus { get; init; }

    /// <summary>
    /// Hides the tweak when the machine cannot use it — for example the NVIDIA profile on
    /// a system with no NVIDIA GPU.
    /// </summary>
    public Func<bool>? IsSupported { get; init; }

    public bool Supported => IsSupported is null || IsSupported();

    /// <summary>Only the actions that apply to this machine.</summary>
    public IEnumerable<RegAction> RelevantActions => Actions.Where(a => a.IsRelevant);
}
