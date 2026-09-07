// ABOUTME: Resolves setup secrets from BFF-owned sources before forwarding privileged setup headers.
// ABOUTME: Protects setup-secret cookies and prevents client-controlled X-Setup-Secret header trust.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Event.Web.BffHosting.Security;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

namespace Explore.Blazor.Services;

public enum SetupSecretSource
{
    None = 0,
    ServerSideSetupSession = 1,
    AnonymousSetupSession = 2,
    ProtectedSetupCookie = 3,
    DevelopmentConfiguration = 4
}

public sealed record SetupSecretResolutionResult(
    bool Found,
    SetupSecretSource Source,
    string? Secret,
    string? FailureCode)
{
    public static SetupSecretResolutionResult NotFound(string failureCode) =>
        new(false, SetupSecretSource.None, null, failureCode);

    public static SetupSecretResolutionResult FoundFrom(SetupSecretSource source, string secret) =>
        new(true, source, secret, null);
}

public interface ISetupSecretCookieProtector
{
    string Protect(string secret);

    bool TryUnprotect(string? protectedValue, out string? secret);
}

public sealed class SetupSecretCookieProtector(IDataProtectionProvider dataProtectionProvider)
    : ISetupSecretCookieProtector
{
    private static readonly TimeSpan CookieLifetime = TimeSpan.FromMinutes(30);
    private readonly ITimeLimitedDataProtector _protector = dataProtectionProvider
        .CreateProtector("Explore.Blazor.SetupSecretCookie.v1")
        .ToTimeLimitedDataProtector();

    public string Protect(string secret) => _protector.Protect(secret.Trim(), CookieLifetime);

    public bool TryUnprotect(string? protectedValue, out string? secret)
    {
        secret = null;
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return false;
        }

        try
        {
            var unprotected = _protector.Unprotect(protectedValue.Trim());
            if (string.IsNullOrWhiteSpace(unprotected))
            {
                return false;
            }

            secret = unprotected.Trim();
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

public interface ISetupSecretResolver
{
    SetupSecretResolutionResult Resolve(
        HttpContext? httpContext = null,
        HttpRequestMessage? outboundRequest = null);
}

public sealed class SetupSecretResolverOptions
{
    public string? DevelopmentSecret { get; set; }
}

public sealed class SetupSecretResolver(
    IHttpContextAccessor httpContextAccessor,
    ISetupSecretSessionService setupSecretSessionService,
    ISetupSecretCookieProtector cookieProtector,
    IOptions<SetupSecretResolverOptions> options,
    IHostEnvironment environment,
    IBffAuthCookieStore? circuitCookies = null) : ISetupSecretResolver
{
    private const string SetupSecretCookieName = "setup-secret";
    private const string SetupSecretSessionCookieName = "setup-secret-session";

    public SetupSecretResolutionResult Resolve(
        HttpContext? httpContext = null,
        HttpRequestMessage? outboundRequest = null)
    {
        var context = httpContext ?? httpContextAccessor.HttpContext;

        var sessionSecret = ResolveSessionSecret(context, outboundRequest);
        if (!string.IsNullOrWhiteSpace(sessionSecret))
        {
            return SetupSecretResolutionResult.FoundFrom(
                SetupSecretSource.ServerSideSetupSession,
                sessionSecret);
        }

        var anonymousSessionSecret = ResolveAnonymousSessionSecret(context);
        if (!string.IsNullOrWhiteSpace(anonymousSessionSecret))
        {
            return SetupSecretResolutionResult.FoundFrom(
                SetupSecretSource.AnonymousSetupSession,
                anonymousSessionSecret);
        }

        var cookieSecret = ResolveProtectedCookieSecret(context);
        if (cookieSecret.Found)
        {
            return cookieSecret;
        }

        var configurationSecret = ResolveDevelopmentConfigurationSecret();
        if (!string.IsNullOrWhiteSpace(configurationSecret))
        {
            return SetupSecretResolutionResult.FoundFrom(
                SetupSecretSource.DevelopmentConfiguration,
                configurationSecret);
        }

        return SetupSecretResolutionResult.NotFound(cookieSecret.FailureCode ?? "setup_secret_not_found");
    }

    private string? ResolveSessionSecret(HttpContext? httpContext, HttpRequestMessage? outboundRequest)
    {
        var userId = ResolveUserId(httpContext?.User);
        if (string.IsNullOrWhiteSpace(userId) && outboundRequest is not null)
        {
            userId = ExtractUserIdFromAuthorizationHeader(outboundRequest);
        }

        return string.IsNullOrWhiteSpace(userId)
            ? null
            : setupSecretSessionService.GetForUser(userId)?.Trim();
    }

    private string? ResolveAnonymousSessionSecret(HttpContext? httpContext)
    {
        var sessionId = ReadCookie(httpContext, SetupSecretSessionCookieName);
        return string.IsNullOrWhiteSpace(sessionId)
            ? null
            : setupSecretSessionService.GetForAnonymousSession(sessionId.Trim())?.Trim();
    }

    private SetupSecretResolutionResult ResolveProtectedCookieSecret(HttpContext? httpContext)
    {
        var protectedCookie = ReadCookie(httpContext, SetupSecretCookieName);
        if (string.IsNullOrWhiteSpace(protectedCookie))
        {
            return SetupSecretResolutionResult.NotFound("setup_secret_cookie_missing");
        }

        return cookieProtector.TryUnprotect(protectedCookie, out var secret)
            ? SetupSecretResolutionResult.FoundFrom(SetupSecretSource.ProtectedSetupCookie, secret!)
            : SetupSecretResolutionResult.NotFound("setup_secret_cookie_invalid");
    }

    private string? ReadCookie(HttpContext? httpContext, string name)
    {
        if (httpContext is not null) return httpContext.Request.Cookies[name];
        if (circuitCookies?.CookieHeader is not { Length: > 0 } header
            || !CookieHeaderValue.TryParseList([header], out var cookies)) return null;
        return cookies.FirstOrDefault(cookie => string.Equals(cookie.Name.Value, name, StringComparison.Ordinal))?.Value.Value;
    }

    private string? ResolveDevelopmentConfigurationSecret()
    {
        if (!IsLocalBootstrapEnvironment(environment.EnvironmentName))
        {
            return null;
        }

        return options.Value.DevelopmentSecret?.Trim();
    }

    private static bool IsLocalBootstrapEnvironment(string? environmentName)
    {
        return string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Local", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveUserId(ClaimsPrincipal? user)
    {
        return user.TryGetSetupSessionIdentity(out var identity) ? identity.PartitionKey : null;
    }

    private static string? ExtractUserIdFromAuthorizationHeader(HttpRequestMessage request)
    {
        var authHeader = request.Headers.Authorization;
        if (authHeader?.Scheme != "Bearer" || string.IsNullOrWhiteSpace(authHeader.Parameter))
        {
            return null;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(authHeader.Parameter))
            {
                return null;
            }

            var jwt = handler.ReadJwtToken(authHeader.Parameter);
            return jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value
                ?? jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                ?? jwt.Claims.FirstOrDefault(c => c.Type == "sid")?.Value;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
    }
}
