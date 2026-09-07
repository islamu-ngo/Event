namespace Explore.Blazor.Client.Contracts.Interop;

public interface IHomeDiscoveryGeolocation : IAsyncDisposable
{
    Task<HomeDiscoveryGeolocationResult> GetCurrentPositionAsync(
        CancellationToken cancellationToken = default);
}
