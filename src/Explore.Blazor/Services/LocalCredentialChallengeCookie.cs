// ABOUTME: Keeps first-use Local credential authority separate from ordinary BFF authentication.
// ABOUTME: Owns the restricted challenge cookie lifecycle without exposing authority to browser code.

using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;

namespace Explore.Blazor.Services;

public sealed class LocalCredentialChallengeCookie
{
    public const string CookieName = "__Secure-ISLAMU.LocalCredentialChallenge";
    public const string CookiePath = "/bff/auth/local";
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(5);
    private const int MaximumTokenLength = 2048;
    private const int MaximumProtectedLength = 3072;
    private readonly ITimeLimitedDataProtector _protector;

    public LocalCredentialChallengeCookie(IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        _protector = dataProtectionProvider
            .CreateProtector("Explore.Blazor.LocalCredentialChallenge.v1")
            .ToTimeLimitedDataProtector();
    }

    public bool TryIssue(HttpContext context, string token, DateTimeOffset expiresAt)
    {
        var now = DateTimeOffset.UtcNow;
        if (!context.Request.IsHttps
            || !IsBoundedToken(token)
            || expiresAt.Offset != TimeSpan.Zero
            || expiresAt <= now
            || expiresAt > now.Add(MaximumLifetime))
        {
            return false;
        }

        var protectedValue = _protector.Protect(token, expiresAt);
        if (protectedValue.Length > MaximumProtectedLength)
        {
            return false;
        }

        context.Response.Cookies.Append(CookieName, protectedValue, new CookieOptions
        {
            Path = CookiePath,
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = expiresAt
        });
        return true;
    }

    public bool TryRead(HttpContext context, out string? token)
    {
        token = null;
        var protectedValue = context.Request.Cookies[CookieName];
        if (!context.Request.IsHttps
            || string.IsNullOrEmpty(protectedValue)
            || protectedValue.Length > MaximumProtectedLength)
        {
            return false;
        }

        try
        {
            var value = _protector.Unprotect(protectedValue, out var expiresAt);
            var now = DateTimeOffset.UtcNow;
            if (!IsBoundedToken(value) || expiresAt <= now || expiresAt > now.Add(MaximumLifetime))
            {
                return false;
            }

            token = value;
            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    public void Delete(HttpContext context) =>
        context.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            Path = CookiePath,
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict
        });

    private static bool IsBoundedToken(string? token) =>
        !string.IsNullOrWhiteSpace(token)
        && token.Length <= MaximumTokenLength
        && !token.Any(char.IsWhiteSpace)
        && !token.Any(char.IsControl);
}
