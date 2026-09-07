using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Services.Auth;

public interface IBffReturnUrlService
{
    string GetSafeReturnUrl(HttpContext context, ILogger logger);

    string BuildLoginRedirectUrl(string returnUrl, string? provider = null, bool challengeError = false);

    string BuildChallengeRedirectUrl(string returnUrl, string? provider);
}

public sealed class BffReturnUrlService : IBffReturnUrlService
{
    public string GetSafeReturnUrl(HttpContext context, ILogger logger)
    {
        var returnUrl = context.Request.Query["returnUrl"].ToString();

        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        if (returnUrl.StartsWith('/') &&
            !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
            !returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            return returnUrl;
        }

        logger.LogWarning("[AuthEndpoints] Invalid returnUrl '{ReturnUrl}' - defaulting to /", returnUrl);
        return "/";
    }

    public string BuildLoginRedirectUrl(string returnUrl, string? provider = null, bool challengeError = false)
    {
        var queryParts = new List<string>
        {
            $"returnUrl={Uri.EscapeDataString(returnUrl)}"
        };

        if (challengeError)
        {
            queryParts.Add("challengeError=1");
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            queryParts.Add($"provider={Uri.EscapeDataString(provider)}");
        }

        return "/login?" + string.Join("&", queryParts);
    }

    public string BuildChallengeRedirectUrl(string returnUrl, string? provider)
    {
        var encodedReturnUrl = Uri.EscapeDataString(returnUrl);

        return string.IsNullOrWhiteSpace(provider)
            ? BuildLoginRedirectUrl(returnUrl)
            : $"/auth/challenge?provider={Uri.EscapeDataString(provider)}&returnUrl={encodedReturnUrl}";
    }
}
