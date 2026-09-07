namespace Explore.Blazor.Client.Contracts.Services.Accessibility;

/// <summary>
/// Manages focus programmatically for accessible navigation and dialog workflows.
/// Provides save/restore pattern for modal dialogs and focus-on-navigate for Blazouter router.
/// </summary>
public interface IAccessibilityFocusService
{
    /// <summary>
    /// Moves focus to the first element matching the CSS selector.
    /// Adds tabindex="-1" if needed for non-focusable elements.
    /// </summary>
    Task FocusAsync(string cssSelector, bool preventScroll = false);

    /// <summary>
    /// Moves focus to a specific element by its DOM ID.
    /// </summary>
    Task FocusByIdAsync(string elementId, bool preventScroll = false);

    /// <summary>
    /// Focuses the main content landmark (id="main-content").
    /// Used by skip-navigation link and as a fallback target.
    /// </summary>
    Task FocusMainContentAsync();

    /// <summary>
    /// Focuses the first h1 element on the page. Used after navigation events.
    /// Falls back to main content if no h1 exists.
    /// </summary>
    Task FocusOnNavigateAsync();

    /// <summary>
    /// Saves a reference to the currently focused element.
    /// Call before opening a modal/dialog/drawer to enable focus restoration on close.
    /// </summary>
    Task SaveFocusAsync();

    /// <summary>
    /// Restores focus to the previously saved element, or falls back through:
    /// saved element → fallbackSelector → main content → body.
    /// Call when closing a modal/dialog/drawer.
    /// </summary>
    Task RestoreFocusAsync(string? fallbackSelector = null);

    /// <summary>
    /// Returns the user's prefers-reduced-motion setting.
    /// </summary>
    Task<string> GetPreferredMotionAsync();
}
