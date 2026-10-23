using Explore.Domain.Common.Localization;

namespace Explore.Infrastructure.Localization;

/// <summary>
/// Bounds TMS response bodies before retry metadata readers or generated clients consume them.
/// </summary>
public sealed class TmsResponseBoundaryHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        try
        {
            await response.Content.LoadIntoBufferAsync(LocalizationBundlePolicy.MaximumBytes, cancellationToken);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}
