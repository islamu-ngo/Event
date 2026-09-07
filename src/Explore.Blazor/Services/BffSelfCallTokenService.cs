using System.Security.Claims;
using System.Security.Cryptography;
using Event.Web.BffHosting.Security;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Blazor.Services;

public static class BffSelfCallHeaders
{
    public const string Token = "X-ISLAMU-BFF-SELF-CALL";
}

public interface IBffSelfCallTokenService
{
    string? Issue(HttpContext? httpContext, HttpRequestMessage outboundRequest);

    bool Validate(HttpContext httpContext);
}

public sealed class BffSelfCallTokenService(
    IDataProtectionProvider dataProtectionProvider,
    ILogger<BffSelfCallTokenService> logger) : IBffSelfCallTokenService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ITimeLimitedDataProtector _protector = dataProtectionProvider
        .CreateProtector("Explore.Blazor.BffSelfCallToken.v1")
        .ToTimeLimitedDataProtector();

    public string? Issue(HttpContext? httpContext, HttpRequestMessage outboundRequest)
    {
        if (httpContext is null)
        {
            return null;
        }

        var userId = ResolveUserId(httpContext.User);
        var path = ResolveOutboundPath(outboundRequest);
        var host = outboundRequest.RequestUri?.IsAbsoluteUri == true
            ? outboundRequest.RequestUri.Authority
            : httpContext.Request.Host.Value;

        if (string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(path)
            || string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var payload = new BffSelfCallTokenPayload(
            Method: outboundRequest.Method.Method,
            Path: path,
            Host: host,
            UserId: userId,
            Nonce: RandomNumberGenerator.GetHexString(16),
            IssuedAtUtc: DateTimeOffset.UtcNow);

        return _protector.Protect(JsonSerializer.Serialize(payload, JsonOptions), TokenLifetime);
    }

    public bool Validate(HttpContext httpContext)
    {
        if (httpContext.User.Identity?.IsAuthenticated != true
            || !httpContext.Request.Headers.TryGetValue(BffSelfCallHeaders.Token, out var values)
            || values.Count != 1
            || string.IsNullOrWhiteSpace(values[0]))
        {
            return false;
        }

        try
        {
            var json = _protector.Unprotect(values[0]!);
            var payload = JsonSerializer.Deserialize<BffSelfCallTokenPayload>(json, JsonOptions);
            if (payload is null)
            {
                return false;
            }

            var expectedUserId = ResolveUserId(httpContext.User);
            var expectedPath = string.Concat(
                httpContext.Request.PathBase.Value,
                httpContext.Request.Path.Value);

            return string.Equals(payload.Method, httpContext.Request.Method, StringComparison.Ordinal)
                && string.Equals(payload.Path, expectedPath, StringComparison.Ordinal)
                && string.Equals(payload.Host, httpContext.Request.Host.Value, StringComparison.OrdinalIgnoreCase)
                && string.Equals(payload.UserId, expectedUserId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(payload.Nonce);
        }
        catch (CryptographicException ex)
        {
            logger.LogDebug(ex, "Rejected invalid BFF self-call token.");
            return false;
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "Rejected malformed BFF self-call token payload.");
            return false;
        }
        catch (ArgumentException ex)
        {
            logger.LogDebug(ex, "Rejected malformed BFF self-call token.");
            return false;
        }
    }

    private static string? ResolveOutboundPath(HttpRequestMessage request)
    {
        if (request.RequestUri is null)
        {
            return null;
        }

        if (request.RequestUri.IsAbsoluteUri)
        {
            return request.RequestUri.AbsolutePath;
        }

        var rawPath = request.RequestUri.OriginalString;
        var queryIndex = rawPath.IndexOf('?');
        if (queryIndex >= 0)
        {
            rawPath = rawPath[..queryIndex];
        }

        return rawPath.StartsWith('/')
            ? rawPath
            : "/" + rawPath;
    }

    private static string? ResolveUserId(ClaimsPrincipal? user) =>
        user.TryGetCircuitSubject(out var subject) ? subject.PartitionKey : null;

    private sealed record BffSelfCallTokenPayload(
        string Method,
        string Path,
        string Host,
        string UserId,
        string Nonce,
        DateTimeOffset IssuedAtUtc);
}
