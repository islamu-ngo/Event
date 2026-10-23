using Explore.API.Configuration;
using Explore.Application.Contracts.Services;
using Microsoft.Extensions.Options;

namespace Explore.API.Middleware;

public sealed class EffectiveHostAuthorityMiddleware(
    RequestDelegate next,
    IOptionsMonitor<EffectiveHostAuthorityOptions> options)
{
    internal const string TenantItemKey = "__effective_host_tenant";

    public async Task InvokeAsync(HttpContext context)
    {
        // Unconsumed headers mean an untrusted peer, malformed chain or hop-limit overflow.
        // Never let a partially consumed chain select an authority.
        if (context.Request.Headers.ContainsKey("X-Forwarded-For")
            || context.Request.Headers.ContainsKey("X-Forwarded-Host")
            || context.Request.Headers.ContainsKey("X-Forwarded-Proto")
            || context.Request.Headers.ContainsKey("X-Forwarded-Prefix")
            || context.Request.Headers.ContainsKey("Forwarded")
            || !(string.Equals(context.Request.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                || string.Equals(context.Request.Scheme, "https", StringComparison.OrdinalIgnoreCase)))
        {
            Reject(context);
            return;
        }

        var authority = options.CurrentValue;
        authority.Validate();
        if (!Uri.TryCreate($"{context.Request.Scheme}://{context.Request.Host.Value}/", UriKind.Absolute, out var effective)
            || effective.UserInfo.Length != 0 || effective.Query.Length != 0 || effective.Fragment.Length != 0
            || effective.AbsolutePath != "/")
        {
            Reject(context);
            return;
        }
        var host = effective.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0 || Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            Reject(context);
            return;
        }

        if (authority.IsInstanceHost(host))
        {
            await next(context);
            return;
        }

        var now = context.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? TimeProvider.System.GetUtcNow();
        var proof = authority.TenantDomains.SingleOrDefault(candidate =>
            candidate.IsCurrent(host, candidate.TenantId, now));
        string? subdomain = null;
        string? subdomainBase = null;
        foreach (var baseDomain in authority.SubdomainBaseDomains)
        {
            var suffix = "." + baseDomain;
            if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            var candidate = host[..^suffix.Length];
            if (candidate.Length > 0 && !candidate.Contains('.'))
            {
                subdomain = candidate;
                subdomainBase = baseDomain;
            }
        }

        if (proof is not null || subdomain is not null)
        {
            await using var scope = context.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            var configuration = await scope.ServiceProvider.GetRequiredService<IResolverConfigService>()
                .GetConfigurationAsync(context.RequestAborted);
            var directory = scope.ServiceProvider.GetRequiredService<ITenantLookupSource>();
            Guid? tenant = null;
            if (proof is not null && configuration.CustomDomainEnabled && configuration.AllowTenantCustomDomains)
            {
                tenant = await directory.GetCurrentDomainTenantAsync(host, customDomain: true, context.RequestAborted);
                if (tenant != proof.TenantId) tenant = null;
            }
            else if (subdomain is not null && configuration.SubdomainEnabled
                && string.Equals(configuration.InstanceBaseDomain.TrimEnd('.'), subdomainBase, StringComparison.OrdinalIgnoreCase))
            {
                tenant = await directory.GetCurrentDomainTenantAsync(subdomain, customDomain: false, context.RequestAborted);
            }

            // A reload or expiry while persistence was awaited cannot consume the old proof.
            if (tenant is { } tenantId && tenantId != Guid.Empty
                && ReferenceEquals(authority, options.CurrentValue)
                && (proof is null || proof.IsCurrent(host, tenantId,
                    context.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? TimeProvider.System.GetUtcNow())))
            {
                context.Items[TenantItemKey] = tenantId;
                await next(context);
                return;
            }
        }

        Reject(context);
    }

    private static void Reject(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.Headers.CacheControl = "no-store";
    }
}
