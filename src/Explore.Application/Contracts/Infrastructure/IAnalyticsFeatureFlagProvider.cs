namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Optional capability for analytics providers that support feature flags.
/// <para>
/// Not all providers support feature flags:
/// - PostHog: Full support (server-side evaluation with local caching).
/// - Plausible: Not supported (returns false).
/// - Rybbit: Not supported (returns false).
/// - RudderStack: Not supported (returns false).
/// - None: Not supported (returns false).
/// </para>
/// <para>
/// Consumers should always handle false as the default — feature flags degrade gracefully
/// when the active provider doesn't support them or when analytics is disabled.
/// </para>
/// </summary>
public interface IAnalyticsFeatureFlagProvider
{
    /// <summary>
    /// Checks if a feature flag is enabled for a specific user.
    /// Returns false if the provider doesn't support feature flags or on error.
    /// </summary>
    /// <param name="featureKey">The feature flag key (e.g., "new-event-flow").</param>
    /// <param name="distinctId">The user to evaluate the flag for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the feature is enabled for this user, false otherwise.</returns>
    Task<bool> IsFeatureEnabledAsync(
        string featureKey,
        string distinctId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the payload associated with a feature flag for a specific user.
    /// Returns null if the provider doesn't support feature flags, the flag doesn't exist, or on error.
    /// </summary>
    /// <param name="featureKey">The feature flag key.</param>
    /// <param name="distinctId">The user to evaluate the flag for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The flag payload (typically a JSON-serializable object), or null.</returns>
    Task<object?> GetFeatureFlagPayloadAsync(
        string featureKey,
        string distinctId,
        CancellationToken cancellationToken = default);
}
