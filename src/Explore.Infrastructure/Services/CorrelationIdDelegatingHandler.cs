// Attached to the CerbosClient HttpClient to enable end-to-end request tracing through the Cerbos PDP.

using System.Diagnostics;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Injects the current <see cref="Activity.Current"/> trace ID as an X-Correlation-ID header
/// on all outgoing HTTP requests. If no activity is active, falls back to a new GUID.
/// </summary>
public class CorrelationIdDelegatingHandler : DelegatingHandler
{
    private const string HeaderName = "X-Correlation-ID";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(HeaderName))
        {
            var correlationId = Activity.Current?.Id ?? Guid.CreateVersion7().ToString();
            request.Headers.TryAddWithoutValidation(HeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
