// ABOUTME: Short-TTL cached onboarding status probe shared by middleware, cookie events, and admin enrichment.
// ABOUTME: Fetches GET /api/InstanceOnboarding/status (AllowAnonymous, fast) and caches the result to avoid request storms.

using Explore.Blazor.Client.Clients;
using Microsoft.Extensions.Caching.Memory;

namespace Explore.Blazor.Services;

/// <summary>
/// Cached onboarding status probe for the BFF. Several components need to know whether
/// instance onboarding is complete (auth entry gate, stale-cookie redirect target,
/// admin enrichment skip). Each sharing the same short-TTL cache prevents redundant
/// outbound calls and cascade timeouts during cold start.
/// </summary>
public interface IBffOnboardingStatusProvider
{
    /// <summary>
    /// Returns the latest known onboarding status, fetching fresh when cache is cold or expired.
    /// Never throws — returns a conservative <see cref="BffOnboardingStatus.Unknown"/> on error
    /// so callers default to the safest behavior (treat as incomplete).
    /// </summary>
    Task<BffOnboardingStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the cached status. Call after onboarding completes so the next request
    /// sees the fresh state immediately.
    /// </summary>
    void Invalidate();
}

/// <summary>Snapshot of onboarding status used by gating logic.</summary>
public enum BffOnboardingDisposition
{
    InteractivePending,
    ConfiguredAdministratorPending,
    Completed,
    Closed
}

public sealed record BffOnboardingStatus(
    bool IsCompleted,
    string? State,
    string? Mode,
    string? Provider,
    long? Generation,
    BffOnboardingDisposition Disposition)
{
    public static BffOnboardingStatus Unknown { get; } = new(
        IsCompleted: false,
        State: null,
        Mode: null,
        Provider: null,
        Generation: null,
        Disposition: BffOnboardingDisposition.Closed);

    public bool AllowsProvider(string provider) =>
        Disposition == BffOnboardingDisposition.ConfiguredAdministratorPending
        && string.Equals(Provider, provider, StringComparison.OrdinalIgnoreCase);
}

public sealed class BffOnboardingStatusProvider : IBffOnboardingStatusProvider
{
    private const string CacheKey = "BffOnboardingStatus";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BffOnboardingStatusProvider> _logger;

    public BffOnboardingStatusProvider(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        ILogger<BffOnboardingStatusProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<BffOnboardingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue<BffOnboardingStatus>(CacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var apiClient = scope.ServiceProvider.GetRequiredService<IInstanceOnboardingClient>();
            var dto = await apiClient.GetInstanceOnboardingStatusAsync(
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var state = dto.State;
            var mode = dto.Mode;
            var configuredProvider = dto.Provider;
            var generation = dto.Generation;
            var isCompleted = dto.IsCompleted == true;
            var status = new BffOnboardingStatus(
                isCompleted,
                state,
                mode,
                configuredProvider,
                generation,
                Classify(isCompleted, state, mode, configuredProvider, generation));

            _cache.Set(CacheKey, status, CacheTtl);
            return status;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Onboarding status probe failed; treating as unknown.");
            return BffOnboardingStatus.Unknown;
        }
    }

    public void Invalidate() => _cache.Remove(CacheKey);

    private static BffOnboardingDisposition Classify(
        bool isCompleted,
        string? state,
        string? mode,
        string? provider,
        long? generation)
    {
        if (generation is null or <= 0)
        {
            return BffOnboardingDisposition.Closed;
        }

        if (isCompleted
            && string.Equals(state, "Completed", StringComparison.Ordinal)
            && (string.Equals(mode, "Interactive", StringComparison.Ordinal)
                && string.IsNullOrEmpty(provider)
                || string.Equals(mode, "ConfiguredAdministrator", StringComparison.Ordinal)
                && (string.IsNullOrEmpty(provider) || provider is "Keycloak" or "Atproto")))
        {
            return BffOnboardingDisposition.Completed;
        }

        if (!isCompleted
            && string.Equals(state, "InteractivePending", StringComparison.Ordinal)
            && string.Equals(mode, "Interactive", StringComparison.Ordinal)
            && (string.IsNullOrEmpty(provider) || provider is "Local" or "Keycloak" or "Atproto"))
        {
            return BffOnboardingDisposition.InteractivePending;
        }

        if (!isCompleted
            && string.Equals(state, "ConfiguredAdministratorPending", StringComparison.Ordinal)
            && string.Equals(mode, "ConfiguredAdministrator", StringComparison.Ordinal)
            && provider is "Keycloak" or "Atproto")
        {
            return BffOnboardingDisposition.ConfiguredAdministratorPending;
        }

        return BffOnboardingDisposition.Closed;
    }
}
