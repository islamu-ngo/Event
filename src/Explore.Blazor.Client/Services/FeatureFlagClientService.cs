using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services;

public interface IFeatureFlagClientService
{
    Task LoadFlagsAsync();
}

public class FeatureFlagClientService : IFeatureFlagClientService
{
    private readonly IFeaturesClient _api;
    private readonly FeatureStateContainer _featureState;
    private readonly ILogger<FeatureFlagClientService> _logger;

    public FeatureFlagClientService(
        IFeaturesClient api,
        FeatureStateContainer featureState,
        ILogger<FeatureFlagClientService> logger)
    {
        _api = api;
        _featureState = featureState;
        _logger = logger;
    }

    public async Task LoadFlagsAsync()
    {
        try
        {
            var response = await _api.GetMyFeatureFlagsAsync();
            var flags = response.ToDictionary(pair => pair.Key, pair => pair.Value);
            _featureState.SetFlags(flags);
            _logger.LogDebug("Loaded {Count} feature flags", flags.Count);
        }
        catch (ApiException ex) when (ex.StatusCode == 401)
        {
            _logger.LogDebug("Feature flags not loaded — user is not authenticated");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load feature flags from API");
        }
    }
}
