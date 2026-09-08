namespace Explore.Blazor.Client.Contracts.Interop;

public interface IBrowserActionInterop
{
    Task<bool> ShareAsync(string title, string url, CancellationToken cancellationToken = default);

    Task<bool> CopyTextAsync(string text, CancellationToken cancellationToken = default);

    Task<bool> ScrollToElementByIdAsync(string elementId, CancellationToken cancellationToken = default);

    Task<bool> DownloadBase64FileAsync(
        string base64Content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<bool> DownloadFileFromUrlAsync(
        string url,
        CancellationToken cancellationToken = default);

    Task<bool> OpenSameOriginNewTabAsync(
        string url,
        CancellationToken cancellationToken = default);

    Task<bool> FocusOpenerAndCloseAsync(CancellationToken cancellationToken = default);

}
