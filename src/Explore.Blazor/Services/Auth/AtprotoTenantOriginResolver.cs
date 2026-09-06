// ABOUTME: Resolves an ATProto login origin to an explicitly configured tenant id and trusted API tenant slug.
// ABOUTME: Keeps browser-controlled tenant values out of OAuth state, bootstrap assertions, and handoff routing.

using Explore.Atproto.Transport;
using Explore.Blazor.Authentication;
using Explore.Blazor.Client.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.Services.Auth;

public sealed record AtprotoTenantOriginBinding(Uri Origin, Guid TenantId, string TenantSlug);

public sealed class AtprotoTenantOriginResolver(
    IOptions<AtprotoAuthenticationOptions> configuredOptions,
    IOptions<TenantConfiguration> tenantConfiguration,
    IHostEnvironment environment)
{
    public AtprotoTenantOriginBinding Resolve(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Resolve(ParseOrigin($"{request.Scheme}://{request.Host.Value}"));
    }

    public AtprotoTenantOriginBinding Resolve(Uri requestedOrigin)
    {
        var origin = NormalizeOrigin(requestedOrigin);
        var options = configuredOptions.Value;
        foreach (var configured in options.TenantOrigins)
        {
            if (TryParseOrigin(configured.Origin, out var configuredOrigin)
                && OriginsEqual(origin, configuredOrigin)
                && configured.TenantId != Guid.Empty
                && IsValidTenantSlug(configured.TenantSlug))
            {
                return new(configuredOrigin, configured.TenantId, configured.TenantSlug.Trim().ToLowerInvariant());
            }
        }

        var canonical = ParseCanonicalOrigin();
        var tenant = tenantConfiguration.Value;
        if (OriginsEqual(origin, canonical) && tenant.DefaultTenantId != Guid.Empty && IsValidTenantSlug(tenant.DefaultTenant))
        {
            return new(canonical, tenant.DefaultTenantId, tenant.DefaultTenant.Trim().ToLowerInvariant());
        }

        throw new InvalidOperationException("The ATProto login origin is not mapped to a tenant.");
    }

    public Uri ParseCanonicalOrigin()
    {
        var publicUrl = configuredOptions.Value.PublicUrl;
        var policy = new AtprotoOutboundPolicy(
            environment.IsDevelopment() && configuredOptions.Value.AllowDevelopmentLoopback);
        if (!AtprotoClientIdentityFactory.TryCreate(
                publicUrl,
                configuredOptions.Value.CallbackPath,
                policy,
                out var identity))
        {
            throw new InvalidOperationException("ATProto canonical client identity is invalid.");
        }

        var callbackUri = new Uri(identity.CallbackUri, UriKind.Absolute);
        return ParseOrigin(callbackUri.GetLeftPart(UriPartial.Authority));
    }

    public Uri NormalizeOrigin(Uri origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return ParseOrigin(origin.AbsoluteUri);
    }

    public static bool OriginsEqual(Uri left, Uri right) =>
        string.Equals(left.AbsoluteUri, right.AbsoluteUri, StringComparison.Ordinal);

    private bool TryParseOrigin(string value, out Uri origin)
    {
        try
        {
            origin = ParseOrigin(value);
            return true;
        }
        catch (ArgumentException)
        {
            origin = null!;
            return false;
        }
    }

    private Uri ParseOrigin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !IsAllowedOriginScheme(uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/")
        {
            throw new ArgumentException("ATProto origin is invalid.", nameof(value));
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute);
    }

    private bool IsAllowedOriginScheme(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        || (environment.IsDevelopment()
            && configuredOptions.Value.AllowDevelopmentLoopback
            && uri.Scheme == Uri.UriSchemeHttp
            && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)));

    private static bool IsValidTenantSlug(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 63
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
        && value[0] != '-'
        && value[^1] != '-';
}
