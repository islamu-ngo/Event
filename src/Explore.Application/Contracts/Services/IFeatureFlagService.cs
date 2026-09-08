using OpenFeature.Model;

namespace Explore.Application.Contracts.Services;

public interface IFeatureFlagService
{
    Task<bool> IsEnabledAsync(string flagKey, bool defaultValue = false, EvaluationContext? context = null, CancellationToken ct = default);
    Task<string> GetStringValueAsync(string flagKey, string defaultValue, EvaluationContext? context = null, CancellationToken ct = default);
    Task<int> GetIntValueAsync(string flagKey, int defaultValue, EvaluationContext? context = null, CancellationToken ct = default);
    Task<Dictionary<string, bool>> GetClientFlagsAsync(EvaluationContext? context = null, CancellationToken ct = default);
}

/// <summary>
/// Central registry of feature flag keys exposed to the Blazor UI via GET /api/features/my-flags.
/// Add new client-visible flags here; server-only flags should NOT appear in this list.
/// </summary>
public static class ClientFeatureFlags
{
    public static readonly IReadOnlyList<string> All =
    [
        "new-dashboard",
        "beta-analytics",
        "event-series",
        "advanced-registration",
    ];
}
