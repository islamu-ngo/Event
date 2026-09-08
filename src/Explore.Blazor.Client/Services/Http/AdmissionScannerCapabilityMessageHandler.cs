using Explore.Blazor.Client.Services.Admissions;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Explore.Blazor.Client.Services.Http;

public sealed class AdmissionScannerCapabilityMessageHandler(
    AdmissionScannerCapabilityState capabilityState) : DelegatingHandler
{
    public const string HeaderName = "X-Admission-Scanner-Capability";
    public const string CheckInPath = "/api/admission/scanner/check-ins";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Post || !IsExactCheckInRequest(request.RequestUri))
        {
            throw new InvalidOperationException(
                "Scanner capability transport is restricted to the scanner check-in endpoint.");
        }

        if (!capabilityState.TryGetCapability(out string? capability))
        {
            throw new InvalidOperationException("No scanner capability is active.");
        }

        request.Headers.Remove("Authorization");
        request.Headers.Remove("Cookie");
        request.Headers.Remove(HeaderName);
        request.Headers.TryAddWithoutValidation(HeaderName, capability);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Omit);
        request.SetBrowserRequestMode(BrowserRequestMode.SameOrigin);
        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsExactCheckInRequest(Uri? uri)
    {
        if (uri is null || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        string path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString;
        return string.Equals(path, CheckInPath, StringComparison.Ordinal);
    }
}
