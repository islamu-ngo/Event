using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Event.Web.BffHosting.Authentication;

public enum EventBffSessionRejectionCategory
{
    CurrentAuthority = 1,
    TokenRefresh = 2
}

public interface IEventBffCookieSessionHandler
{
    Task OnSigningInAsync(CookieSigningInContext context);

    Task<bool> ValidatePrincipalAsync(CookieValidatePrincipalContext context);

    Task OnTokenRefreshSucceededAsync(
        CookieValidatePrincipalContext context,
        IReadOnlyList<AuthenticationToken> refreshedTokens);

    Task OnTokenRefreshRejectedAsync(
        CookieValidatePrincipalContext context,
        EventBffSessionRejectionCategory category,
        string reason);

    Task<bool> TryRedirectRejectedHtmlNavigationAsync(CookieValidatePrincipalContext context, string reason);
}

public sealed class NoopEventBffCookieSessionHandler : IEventBffCookieSessionHandler
{
    public Task OnSigningInAsync(CookieSigningInContext context) => Task.CompletedTask;

    public Task<bool> ValidatePrincipalAsync(CookieValidatePrincipalContext context) => Task.FromResult(true);

    public Task OnTokenRefreshSucceededAsync(
        CookieValidatePrincipalContext context,
        IReadOnlyList<AuthenticationToken> refreshedTokens) => Task.CompletedTask;

    public Task OnTokenRefreshRejectedAsync(
        CookieValidatePrincipalContext context,
        EventBffSessionRejectionCategory category,
        string reason) =>
        Task.CompletedTask;

    public Task<bool> TryRedirectRejectedHtmlNavigationAsync(
        CookieValidatePrincipalContext context,
        string reason) => Task.FromResult(false);
}

public class EventBffTokenRefreshCookieEvents(
    IOptionsMonitor<OpenIdConnectOptions> oidcOptionsMonitor,
    IHttpClientFactory httpClientFactory,
    IEventBffCookieSessionHandler sessionHandler,
    ILogger<EventBffTokenRefreshCookieEvents> logger)
    : CookieAuthenticationEvents
{
    internal const string TokenRefreshHttpClientName = "Event.Web.BffHosting.TokenRefresh";

    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromSeconds(60);
    private static readonly Action<ILogger, Exception?> LogTokensRefreshed =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1000, "TokensRefreshed"),
            "[TokenRefresh] Tokens refreshed successfully.");
    private static readonly Action<ILogger, bool, Exception?> LogTokenExpiryCheck =
        LoggerMessage.Define<bool>(
            LogLevel.Debug,
            new EventId(1001, "TokenExpiryCheck"),
            "[TokenRefresh] Token expiry check: IsExpired={IsExpired}");
    private static readonly Action<ILogger, Exception?> LogCallingTokenEndpoint =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(1002, "CallingTokenEndpoint"),
            "[TokenRefresh] Calling token endpoint.");

    public override async Task SigningIn(CookieSigningInContext context)
    {
        await sessionHandler.OnSigningInAsync(context);
        await base.SigningIn(context);
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (!await sessionHandler.ValidatePrincipalAsync(context))
        {
            await RejectAndSignOutAsync(
                context: context,
                category: EventBffSessionRejectionCategory.CurrentAuthority,
                reason: "local_session_rejected");
            return;
        }

        var accessToken = context.Properties.GetTokenValue("access_token");
        if (string.IsNullOrEmpty(accessToken))
        {
            return;
        }

        if (!IsTokenExpiredOrNearExpiry(accessToken))
        {
            return;
        }

        var refreshToken = context.Properties.GetTokenValue("refresh_token");
        if (string.IsNullOrEmpty(refreshToken))
        {
            logger.LogWarning(
                "[TokenRefresh] Access token expired but no refresh_token available. Token will be forwarded as-is; API may reject it with 401.");
            return;
        }

        var schemeName = context.Properties.Items.TryGetValue(
            EventBffAuthenticationConstants.OidcSchemePropertyKey,
            out var scheme)
            ? scheme
            : null;

        try
        {
            var result = await RefreshAccessTokenAsync(schemeName, refreshToken, context);
            if (result.Tokens is null)
            {
                logger.LogWarning(
                    "[TokenRefresh] Refresh failed (reason={Reason}) — signing out",
                    result.FailureReason);
                await RejectAndSignOutAsync(
                    context: context,
                    category: EventBffSessionRejectionCategory.TokenRefresh,
                    reason: result.FailureReason ?? "refresh_failed");
                return;
            }

            context.Properties.StoreTokens(result.Tokens);
            await sessionHandler.OnTokenRefreshSucceededAsync(context, result.Tokens);
            context.ShouldRenew = true;

            LogTokensRefreshed(logger, null);
        }
        catch (Exception)
        {
            logger.LogError("[TokenRefresh] Refresh failed. FailureCategory={FailureCategory} — signing out", "refresh_exception");
            await RejectAndSignOutAsync(
                context: context,
                category: EventBffSessionRejectionCategory.TokenRefresh,
                reason: "refresh_exception");
        }
    }

    private async Task RejectAndSignOutAsync(
        CookieValidatePrincipalContext context,
        EventBffSessionRejectionCategory category,
        string reason)
    {
        context.HttpContext.Items[EventBffAuthenticationConstants.TokenRefreshRejectedItemKey] = true;
        await sessionHandler.OnTokenRefreshRejectedAsync(context: context, category: category, reason: reason);
        context.RejectPrincipal();

        try
        {
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
        catch (Exception)
        {
            logger.LogWarning("[TokenRefresh] SignOutAsync failed. FailureCategory={FailureCategory}", "signout_failed");
        }

        if (context.HttpContext.Response.HasStarted || !IsHtmlNavigation(context.HttpContext.Request))
        {
            return;
        }

        if (await sessionHandler.TryRedirectRejectedHtmlNavigationAsync(context, reason))
        {
            return;
        }

        var returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString;
        var encoded = Uri.EscapeDataString(returnUrl);
        context.HttpContext.Response.Redirect($"/login?returnUrl={encoded}&session=expired&reason={reason}");
    }

    private static bool IsHtmlNavigation(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method))
        {
            return false;
        }

        var accept = request.Headers.Accept.ToString();
        return accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsTokenExpiredOrNearExpiry(string accessToken)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(accessToken))
            {
                logger.LogWarning("[TokenRefresh] Cannot read access token JWT — treating as expired");
                return true;
            }

            var jwt = handler.ReadJwtToken(accessToken);
            var validTo = jwt.ValidTo;
            var now = DateTime.UtcNow;
            var threshold = now.Add(RefreshBuffer);
            var isExpired = validTo <= threshold;

            LogTokenExpiryCheck(logger, isExpired, null);

            return isExpired;
        }
        catch (Exception)
        {
            logger.LogWarning("[TokenRefresh] Failed to parse access token JWT. FailureCategory={FailureCategory}", "invalid_access_token");
            return true;
        }
    }

    private async Task<RefreshResult> RefreshAccessTokenAsync(
        string? schemeName,
        string refreshToken,
        CookieValidatePrincipalContext context)
    {
        if (string.IsNullOrEmpty(schemeName))
        {
            logger.LogWarning("[TokenRefresh] No oidc_scheme stored in cookie — cannot determine token endpoint");
            return RefreshResult.Failure("missing_scheme");
        }

        OpenIdConnectOptions oidcOptions;
        try
        {
            oidcOptions = oidcOptionsMonitor.Get(schemeName);
        }
        catch (Exception)
        {
            logger.LogWarning("[TokenRefresh] Failed to resolve OIDC options. FailureCategory={FailureCategory}", "oidc_options_error");
            return RefreshResult.Failure("oidc_options_error");
        }

        if (oidcOptions.ConfigurationManager is null)
        {
            logger.LogWarning("[TokenRefresh] ConfigurationManager is unavailable.");
            return RefreshResult.Failure("no_configuration_manager");
        }

        var oidcConfig = await oidcOptions.ConfigurationManager.GetConfigurationAsync(context.HttpContext.RequestAborted);
        var tokenEndpoint = oidcConfig.TokenEndpoint;

        if (string.IsNullOrEmpty(tokenEndpoint))
        {
            logger.LogWarning("[TokenRefresh] Token endpoint not found in OIDC config.");
            return RefreshResult.Failure("no_token_endpoint");
        }

        if (string.IsNullOrWhiteSpace(oidcOptions.ClientId))
        {
            logger.LogWarning("[TokenRefresh] ClientId missing.");
            return RefreshResult.Failure("missing_client_id");
        }

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = oidcOptions.ClientId,
        };

        if (!string.IsNullOrEmpty(oidcOptions.ClientSecret))
        {
            parameters["client_secret"] = oidcOptions.ClientSecret;
        }

        var httpClient = httpClientFactory.CreateClient(TokenRefreshHttpClientName);
        LogCallingTokenEndpoint(logger, null);

        using var content = new FormUrlEncodedContent(parameters);
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint) { Content = content };
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);
        const int maximumResponseBytes = 64 * 1024;
        if (response.Content.Headers.ContentLength > maximumResponseBytes)
            throw new InvalidDataException("Token response exceeds its limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(context.HttpContext.RequestAborted);
        byte[] bytes = new byte[maximumResponseBytes + 1];
        int length = 0;
        while (length < bytes.Length)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(length), context.HttpContext.RequestAborted);
            if (read == 0)
                break;
            length += read;
        }
        if (length > maximumResponseBytes)
            throw new InvalidDataException("Token response exceeds its limit.");
        if (!response.IsSuccessStatusCode)
        {
            var reason = ParseOidcErrorCode(bytes.AsMemory(0, length)) ?? "token_endpoint_rejected";
            logger.LogWarning(
                "[TokenRefresh] Token endpoint returned {StatusCode} (error={Error}, hasBody={HasBody})",
                response.StatusCode,
                reason,
                length > 0);
            return RefreshResult.Failure(reason);
        }

        using var doc = JsonDocument.Parse(bytes.AsMemory(0, length), new JsonDocumentOptions { MaxDepth = 16 });
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("access_token", out var newAccessTokenElement)
            || newAccessTokenElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(newAccessTokenElement.GetString()))
        {
            logger.LogWarning("[TokenRefresh] Response missing access_token.");
            return RefreshResult.Failure("missing_access_token");
        }

        var tokens = new List<AuthenticationToken>
        {
            new() { Name = "access_token", Value = newAccessTokenElement.GetString()! }
        };

        if (root.TryGetProperty("refresh_token", out var newRefreshElement) &&
            !string.IsNullOrEmpty(newRefreshElement.GetString()))
        {
            tokens.Add(new AuthenticationToken { Name = "refresh_token", Value = newRefreshElement.GetString()! });
        }
        else
        {
            tokens.Add(new AuthenticationToken { Name = "refresh_token", Value = refreshToken });
        }

        if (root.TryGetProperty("id_token", out var newIdTokenElement) &&
            !string.IsNullOrEmpty(newIdTokenElement.GetString()))
        {
            tokens.Add(new AuthenticationToken { Name = "id_token", Value = newIdTokenElement.GetString()! });
        }
        else
        {
            var existingIdToken = context.Properties.GetTokenValue("id_token");
            if (!string.IsNullOrEmpty(existingIdToken))
            {
                tokens.Add(new AuthenticationToken { Name = "id_token", Value = existingIdToken });
            }
        }

        if (root.TryGetProperty("expires_in", out var expiresInElement) && expiresInElement.TryGetInt32(out var expiresIn))
        {
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            tokens.Add(new AuthenticationToken { Name = "expires_at", Value = expiresAt.ToString("o") });
        }

        return RefreshResult.Success(tokens);
    }

    private static string? ParseOidcErrorCode(ReadOnlyMemory<byte> body)
    {
        if (body.IsEmpty)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var errorElement)
                && errorElement.ValueKind == JsonValueKind.String)
            {
                return NormalizeOidcErrorCode(errorElement.GetString());
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string? NormalizeOidcErrorCode(string? errorCode)
    {
        return errorCode switch
        {
            "invalid_request" or "invalid_client" or "invalid_grant" or "unauthorized_client"
                or "unsupported_grant_type" or "invalid_scope" or "server_error"
                or "temporarily_unavailable" => errorCode,
            _ => null
        };
    }

    private readonly record struct RefreshResult(List<AuthenticationToken>? Tokens, string? FailureReason)
    {
        public static RefreshResult Success(List<AuthenticationToken> tokens) => new(tokens, null);

        public static RefreshResult Failure(string reason) => new(null, reason);
    }

}
