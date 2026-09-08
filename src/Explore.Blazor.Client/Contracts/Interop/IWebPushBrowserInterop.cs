namespace Explore.Blazor.Client.Contracts.Interop;

public interface IWebPushBrowserInterop : IAsyncDisposable
{
    Task<WebPushBrowserState> GetStateAsync(CancellationToken cancellationToken = default);

    Task<WebPushBrowserSubscription?> SubscribeAsync(
        string applicationServerKey,
        CancellationToken cancellationToken = default);

    Task<bool> UnsubscribeAsync(CancellationToken cancellationToken = default);
}
