namespace Explore.Blazor.Client.Contracts.Services.Accessibility;

/// <summary>
/// Announces dynamic content changes to screen readers via ARIA live regions.
/// Uses polite (non-interrupting) or assertive (immediate) politeness levels.
/// </summary>
public interface IAccessibilityAnnouncerService
{
    /// <summary>
    /// Announces a message politely (waits for current speech to finish).
    /// Use for status updates, search results count, toast confirmations.
    /// </summary>
    Task AnnouncePoliteAsync(string message);

    /// <summary>
    /// Announces a message assertively (interrupts current speech immediately).
    /// Use only for critical alerts: errors, session expiry, destructive action confirmations.
    /// </summary>
    Task AnnounceAssertiveAsync(string message);
}
