namespace OptiGames.ViewModels;

/// <summary>
/// A destination in the sidebar. <see cref="OnActivated"/> is where a page refreshes its
/// data — pages are constructed once and reused, so a constructor is the wrong place for
/// anything that can go stale.
/// </summary>
public abstract class PageViewModel : ObservableObject
{
    public required string Title { get; init; }

    /// <summary>Resource key of the sidebar icon geometry.</summary>
    public required string Icon { get; init; }

    /// <summary>Pages below the divider (Help, Settings) sit in the sidebar's footer group.</summary>
    public bool IsFooter { get; init; }

    /// <summary>
    /// Drives the sidebar highlight. Owned by the page rather than by a ListBox, because the
    /// sidebar is split into two groups and a control-owned selection cannot span both.
    /// </summary>
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; internal set => Set(ref _isSelected, value); }

    public virtual void OnActivated() { }
}
