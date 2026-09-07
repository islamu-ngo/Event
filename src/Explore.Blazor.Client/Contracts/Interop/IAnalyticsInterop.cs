using Explore.Blazor.Client.Models.Analytics;

namespace Explore.Blazor.Client.Contracts.Interop;

public interface IAnalyticsInterop
{
    Task InitAsync(string analyticsProvider, bool analyticsEnabled, string analyticsConsentMode, string analyticsTransportMode, bool allowIdentify, string? apiKey, string? endpointUrl, PosthogClientBootstrapModel? posthogOptions = null);
    Task TrackAsync(string eventName, IDictionary<string, object>? properties = null);
    Task IdentifyAsync(string distinctId, IDictionary<string, object>? traits = null);
    Task PageViewAsync(string pagePath, IDictionary<string, object>? properties = null);
    Task OptInCapturingAsync();
    Task OptOutCapturingAsync();
}
