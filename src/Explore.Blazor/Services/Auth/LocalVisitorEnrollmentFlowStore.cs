using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Distributed;

namespace Explore.Blazor.Services.Auth;

public sealed record LocalVisitorEnrollmentTarget(string Origin, string Host, string? TenantSlug, string PathBase)
{
    public override string ToString() => nameof(LocalVisitorEnrollmentTarget);
}

public sealed record LocalVisitorEnrollmentFlow(
    string Correlation,
    Guid OperationId,
    string Proof,
    string? EnrollmentIntent,
    LocalVisitorEnrollmentTarget Target,
    string ReturnPath,
    DateTimeOffset ExpiresAt)
{
    public override string ToString() => nameof(LocalVisitorEnrollmentFlow);
}

/// <summary>
/// Keeps enrollment authority in the server cache. The browser holds only a protected
/// random correlation. Neither reserve reconciliation nor registration extends the deadline.
/// </summary>
public sealed class LocalVisitorEnrollmentFlowStore(
    IDistributedCache cache, IDataProtectionProvider protection, TimeProvider clock)
{
    public const string CookieName = "__Host-ISLAMU.LocalVisitorEnrollment";
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private const string CachePrefix = "bff-local-enrollment:";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector _cookieProtector = protection.CreateProtector("Explore.LocalVisitorEnrollment.Cookie.v1");
    private readonly IDataProtector _cacheProtector = protection.CreateProtector("Explore.LocalVisitorEnrollment.Cache.v1");

    public LocalVisitorEnrollmentTarget? CaptureTarget(HttpContext context)
    {
        var request = context.Request;
        if (!request.IsHttps || !request.Host.HasValue || context.Connection.RemoteIpAddress is null
            || !Uri.TryCreate($"https://{request.Host.Value}/", UriKind.Absolute, out var origin)
            || !string.IsNullOrEmpty(origin.UserInfo))
            return null;

        var originHeader = request.Headers.Origin;
        if (originHeader.Count != 1
            || !string.Equals(originHeader[0], origin.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal))
            return null;

        var slug = context.RequestServices.GetRequiredService<ITenantRouteContextAccessor>().TenantSlug;
        if (slug is not null && (slug.Length is < 1 or > 500
            || slug.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')))
            return null;

        var pathBase = request.PathBase.Value ?? string.Empty;
        if (pathBase.Length > 1024 || pathBase.Length != 0 && !IsSafeLocalPath(pathBase))
            return null;

        return new(origin.GetLeftPart(UriPartial.Authority), request.Host.Value, slug, pathBase);
    }

    public bool Matches(HttpContext context, LocalVisitorEnrollmentFlow flow, bool requireCookie = true) =>
        flow.ExpiresAt > clock.GetUtcNow()
        && CaptureTarget(context) == flow.Target
        && (!requireCookie || ReadCorrelation(context) == flow.Correlation);

    public async Task<LocalVisitorEnrollmentFlow?> ReadAsync(HttpContext context, CancellationToken ct)
    {
        var correlation = ReadCorrelation(context);
        if (correlation is null) return null;
        var payload = await cache.GetAsync(CachePrefix + correlation, ct);
        if (payload is null) return null;
        if (await cache.GetAsync(CachePrefix + correlation + ":completed", ct) is not null) return null;
        try
        {
            var flow = JsonSerializer.Deserialize<LocalVisitorEnrollmentFlow>(_cacheProtector.Unprotect(payload), JsonOptions);
            return flow is not null && flow.Correlation == correlation && Matches(context, flow) ? flow : null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }

    public async Task<LocalVisitorEnrollmentFlow> CreateAsync(
        HttpContext context, LocalVisitorEnrollmentTarget target, string returnPath, CancellationToken ct)
    {
        var flow = new LocalVisitorEnrollmentFlow(
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),
            Guid.CreateVersion7(),
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),
            null, target, returnPath, clock.GetUtcNow().Add(MaximumLifetime));
        context.Items[CookieName] = flow.Correlation;
        await SaveAsync(flow, ct);
        if (Matches(context, flow, requireCookie: false))
        {
            context.Response.Cookies.Append(CookieName, _cookieProtector.Protect(flow.Correlation), CookieOptions(flow.ExpiresAt));
        }
        return flow;
    }

    public Task SaveAsync(LocalVisitorEnrollmentFlow flow, CancellationToken ct) =>
        cache.SetAsync(CachePrefix + flow.Correlation,
            _cacheProtector.Protect(JsonSerializer.SerializeToUtf8Bytes(flow, JsonOptions)),
            new DistributedCacheEntryOptions { AbsoluteExpiration = flow.ExpiresAt }, ct);

    public async Task ClearAsync(HttpContext context, CancellationToken ct)
    {
        var correlation = ReadCorrelation(context) ?? context.Items[CookieName] as string;
        DeleteCookie(context);
        if (correlation is not null) await cache.RemoveAsync(CachePrefix + correlation, ct);
    }

    public async Task CompleteAsync(HttpContext context, LocalVisitorEnrollmentFlow flow, CancellationToken ct)
    {
        DeleteCookie(context);
        // A delayed reserve response may still write its original cache snapshot.
        // A separate deadline-bound marker prevents that write from reviving a completed flow.
        await cache.SetAsync(CachePrefix + flow.Correlation + ":completed", [1],
            new DistributedCacheEntryOptions { AbsoluteExpiration = flow.ExpiresAt }, ct);
        await cache.RemoveAsync(CachePrefix + flow.Correlation, ct);
    }

    public static bool IsSafeLocalPath(string? path) =>
        !string.IsNullOrEmpty(path) && path.Length <= 2048 && path.StartsWith('/')
        && RedirectHttpResult.IsLocalUrl(path)
        && !path.Any(character => char.IsControl(character) || character is '\\' or '%' or '#');

    private string? ReadCorrelation(HttpContext context)
    {
        var value = context.Request.Cookies[CookieName];
        if (!context.Request.IsHttps || string.IsNullOrEmpty(value) || value.Length > 1024) return null;
        try
        {
            var correlation = _cookieProtector.Unprotect(value);
            return correlation.Length == 64 && correlation.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
                ? correlation : null;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static CookieOptions CookieOptions(DateTimeOffset? expiresAt = null) => new()
    {
        Path = "/",
        Secure = true,
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Expires = expiresAt,
        IsEssential = true
    };

    private static void DeleteCookie(HttpContext context) =>
        context.Response.Cookies.Delete(CookieName, CookieOptions());
}
