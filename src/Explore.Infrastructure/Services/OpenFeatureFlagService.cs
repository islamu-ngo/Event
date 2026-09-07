using Explore.Application.Contracts.Services;
using OpenFeature;
using OpenFeature.Model;

namespace Explore.Infrastructure.Services;

public class OpenFeatureFlagService : IFeatureFlagService
{
    private readonly IFeatureClient _client;

    public OpenFeatureFlagService(IFeatureClient client)
    {
        _client = client;
    }

    public async Task<bool> IsEnabledAsync(string flagKey, bool defaultValue = false, EvaluationContext? context = null, CancellationToken ct = default)
    {
        return await _client.GetBooleanValueAsync(flagKey, defaultValue, context, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<string> GetStringValueAsync(string flagKey, string defaultValue, EvaluationContext? context = null, CancellationToken ct = default)
    {
        return await _client.GetStringValueAsync(flagKey, defaultValue, context, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<int> GetIntValueAsync(string flagKey, int defaultValue, EvaluationContext? context = null, CancellationToken ct = default)
    {
        return await _client.GetIntegerValueAsync(flagKey, defaultValue, context, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, bool>> GetClientFlagsAsync(EvaluationContext? context = null, CancellationToken ct = default)
    {
        var flags = new Dictionary<string, bool>(ClientFeatureFlags.All.Count);
        foreach (var key in ClientFeatureFlags.All)
        {
            flags[key] = await _client.GetBooleanValueAsync(key, false, context, cancellationToken: ct).ConfigureAwait(false);
        }
        return flags;
    }
}
