using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services.Http;

public sealed class BffUnauthorizedHandler : DelegatingHandler
{
    private static readonly string[] AnonymousApiPathPrefixes =
    [
        "/api/publicexperience/settings",
        "/api/instanceonboarding/status"
    ];

    private readonly NavigationManager _nav;
    private readonly ILogger<BffUnauthorizedHandler> _logger;

    public BffUnauthorizedHandler(NavigationManager nav, ILogger<BffUnauthorizedHandler> logger)
    {
        _nav = nav;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BFF handler: request failed for {Method} {Uri}", request.Method, request.RequestUri);
            throw;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var path = GetRequestPath(request.RequestUri);
            if (!TryGetCurrentRelativePath(out var currentRelativePath))
            {
                _logger.LogDebug("BFF handler: suppressing 401 redirect before navigation manager initialization for {Method} {Uri}", request.Method, request.RequestUri);
                return response;
            }

            if (string.IsNullOrWhiteSpace(currentRelativePath))
            {
                currentRelativePath = "/";
            }
            else if (!currentRelativePath.StartsWith('/'))
            {
                currentRelativePath = "/" + currentRelativePath;
            }
            else
            {
                var queryIndex = currentRelativePath.IndexOf('?', System.StringComparison.Ordinal);
                if (queryIndex >= 0)
                {
                    currentRelativePath = currentRelativePath[..queryIndex];
                }
            }

            // Avoid redirect loops when the request itself is part of authentication endpoints.
            if (path.StartsWith("/login", System.StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/logout", System.StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/auth", System.StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("BFF handler: received 401 from auth endpoint {Method} {Uri}", request.Method, request.RequestUri);
                return response;
            }

            // Avoid redirect loops when the user is already on auth pages.
            if (currentRelativePath.StartsWith("/login", System.StringComparison.OrdinalIgnoreCase) ||
                currentRelativePath.StartsWith("/logout", System.StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("BFF handler: suppressing 401 redirect while on auth page {CurrentPath}", currentRelativePath);
                return response;
            }

            // Keep anonymous startup/public endpoints non-interruptive.
            if (AnonymousApiPathPrefixes.Any(prefix => path.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogDebug("BFF handler: received 401 from anonymous endpoint {Method} {Uri}", request.Method, request.RequestUri);
                return response;
            }

            _logger.LogWarning("BFF handler: received 401 for {Method} {Uri} - redirecting to /login", request.Method, request.RequestUri);

            if (!TryGetCurrentRelativePath(out var relativeCurrentPath))
            {
                return response;
            }

            if (string.IsNullOrWhiteSpace(relativeCurrentPath))
            {
                relativeCurrentPath = "/";
            }
            else if (!relativeCurrentPath.StartsWith('/'))
            {
                relativeCurrentPath = "/" + relativeCurrentPath;
            }

            var returnUrl = Uri.EscapeDataString(relativeCurrentPath);
            _nav.NavigateTo($"/login?returnUrl={returnUrl}", forceLoad: true);
        }

        return response;
    }

    private bool TryGetCurrentRelativePath(out string currentRelativePath)
    {
        currentRelativePath = string.Empty;
        try
        {
            currentRelativePath = _nav.ToBaseRelativePath(_nav.Uri);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "BFF handler: NavigationManager is not initialized.");
            return false;
        }
    }

    private static string GetRequestPath(Uri? uri)
    {
        if (uri is null)
        {
            return string.Empty;
        }

        if (uri.IsAbsoluteUri)
        {
            return uri.AbsolutePath;
        }

        var value = uri.OriginalString;
        var queryIndex = value.IndexOf('?', StringComparison.Ordinal);
        return queryIndex >= 0 ? value[..queryIndex] : value;
    }
}
