using Explore.API.Authentication;
using Explore.API.Attributes;
using Explore.API.Configuration;
using Explore.Application.Constants;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Explore.API.Middleware;

public sealed class ApiTenantResolutionMiddleware
{
    private static readonly Guid FallbackDefaultTenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    internal const string RequestedTenantIdItemKey = "__requested_tenant_id";

    private readonly RequestDelegate _next;
    private readonly DeploymentSettings _deploymentSettings;

    public ApiTenantResolutionMiddleware(RequestDelegate next, IOptions<DeploymentSettings> deploymentSettings)
    {
        _next = next;
        _deploymentSettings = deploymentSettings.Value;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IResolverConfigService resolverConfigService,
        ITenantSlugCache tenantSlugCache,
        ITenantContextAccessor tenantContextAccessor,
        IProblemDetailsService problemDetailsService,
        IDeploymentModeProvider deploymentModeProvider,
        IOptions<McpAdapterSettings> mcpAdapterOptions,
        ITenantLifecycleAccessService lifecycle)
    {
        var isMcpPath = IsEnabledMcpPath(context, mcpAdapterOptions.Value);
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) && !isMcpPath)
        {
            await _next(context);
            return;
        }

        if (tenantContextAccessor.IsResolved)
        {
            await _next(context);
            return;
        }

        if (IsTenantExemptPath(context.Request)
            || context.GetEndpoint()?.Metadata.GetMetadata<InstanceManagementAttribute>() is not null)
        {
            await _next(context);
            return;
        }

        if (await deploymentModeProvider.IsSingleTenantAsync(context.RequestAborted))
        {
            var defaultTenantId = _deploymentSettings.DefaultTenantId != Guid.Empty
                ? _deploymentSettings.DefaultTenantId
                : FallbackDefaultTenantId;

            tenantContextAccessor.SetTenant(defaultTenantId);
            await _next(context);
            return;
        }

        var configuration = await resolverConfigService.GetConfigurationAsync(context.RequestAborted);

        bool privateSessionRead = TenantLifecycleAccessMiddleware.IsPrivateAdministratorSessionRead(context.GetEndpoint());
        string? requestedSlug = context.Request.Headers[TenantHeaderNames.TenantSlug].FirstOrDefault();
        bool explicitlySelectedSessionTenant = privateSessionRead && !string.IsNullOrWhiteSpace(requestedSlug);
        var resolvedTenantId = await ResolveFromSlugHeaderAsync(context, tenantSlugCache);
        if (!explicitlySelectedSessionTenant)
        {
            resolvedTenantId ??= await ResolveFromHostAsync(context, configuration, tenantSlugCache);
        }

        var hasApiKeyHeader = ApiKeyHeaderReader.HasNonEmptyApiKey(context.Request);
        // The public routing cache intentionally excludes unpublished tenants. Bind only an
        // explicitly named bootstrap directory for these session endpoints; this grants no authority.
        if (resolvedTenantId is null && !hasApiKeyHeader && !string.IsNullOrWhiteSpace(requestedSlug)
            && (TenantLifecycleAccessMiddleware.IsUserSynchronization(context.GetEndpoint())
                || TenantLifecycleAccessMiddleware.IsPrivateAdministratorSessionRead(context.GetEndpoint())))
        {
            resolvedTenantId = await lifecycle.ResolveConfiguredAdministratorTenantAsync(requestedSlug, context.RequestAborted);
        }

        bool unresolvedExplicitSessionTenant = explicitlySelectedSessionTenant && resolvedTenantId is null;
        if (hasApiKeyHeader && !isMcpPath && !unresolvedExplicitSessionTenant)
        {
            if (resolvedTenantId is Guid requestedTenantId && requestedTenantId != Guid.Empty)
            {
                context.Items[RequestedTenantIdItemKey] = requestedTenantId;
            }

            await _next(context);
            return;
        }

        if (resolvedTenantId is Guid tenantId && tenantId != Guid.Empty)
        {
            tenantContextAccessor.SetTenant(tenantId);
            if (hasApiKeyHeader)
            {
                context.Items[RequestedTenantIdItemKey] = tenantId;
            }

            await _next(context);
            return;
        }

        if (privateSessionRead && !explicitlySelectedSessionTenant && !hasApiKeyHeader
            || hasApiKeyHeader && isMcpPath && !unresolvedExplicitSessionTenant)
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.Headers.CacheControl = "no-store";

        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Tenant not resolved",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                Detail = "The tenant could not be resolved for this request.",
                Instance = context.Request.Path
            }
        });
    }

    private static async Task<Guid?> ResolveFromSlugHeaderAsync(HttpContext context, ITenantSlugCache tenantSlugCache)
    {
        var tenantSlug = context.Request.Headers[TenantHeaderNames.TenantSlug].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(tenantSlug))
        {
            return null;
        }

        return await tenantSlugCache.GetTenantIdBySlugAsync(tenantSlug, context.RequestAborted);
    }

    private static async Task<Guid?> ResolveFromHostAsync(HttpContext context, ResolverConfigurationDto configuration, ITenantSlugCache tenantSlugCache)
    {
        var host = GetRequestHost(context);
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        if (configuration.CustomDomainEnabled && configuration.AllowTenantCustomDomains)
        {
            var customDomainTenantId = await tenantSlugCache.GetTenantIdByDomainAsync(host, context.RequestAborted);
            if (customDomainTenantId is Guid resolvedCustomDomainTenantId && resolvedCustomDomainTenantId != Guid.Empty)
            {
                return resolvedCustomDomainTenantId;
            }
        }

        if (!configuration.SubdomainEnabled || string.IsNullOrWhiteSpace(configuration.InstanceBaseDomain))
        {
            return null;
        }

        var baseDomain = NormalizeHost(configuration.InstanceBaseDomain);
        var subdomain = ExtractSubdomain(host, baseDomain);
        if (string.IsNullOrWhiteSpace(subdomain))
        {
            return null;
        }

        return await tenantSlugCache.GetTenantIdByDomainAsync(subdomain, context.RequestAborted);
    }

    private static string GetRequestHost(HttpContext context)
    {
        return NormalizeHost(context.Request.Host.Host) ?? string.Empty;
    }

    internal static bool IsTenantExemptPath(HttpRequest request)
    {
        PathString path = request.Path;
        return AtprotoTransientAuthenticationDefaults.IsPrivatePath(path)
            || path.Equals(new PathString("/api/auth/local/login"), StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/InstanceOnboarding", StringComparison.OrdinalIgnoreCase)
            || path.Equals(new PathString("/api/operator-identity-metadata"), StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/System", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/admin/instance", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/managed-provider-provisioning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                path.Value,
                "/api/instance/settings/resolver-config",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEnabledMcpPath(HttpContext context, McpAdapterSettings settings)
    {
        return settings.Enabled &&
               !string.IsNullOrWhiteSpace(settings.EndpointPath) &&
               context.Request.Path.StartsWithSegments(
                   settings.EndpointPath,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeHost(string? host)
    {
        return string.IsNullOrWhiteSpace(host)
            ? null
            : host.Trim().TrimEnd('.').ToLowerInvariant();
    }

    private static string? ExtractSubdomain(string host, string? baseDomain)
    {
        if (string.IsNullOrWhiteSpace(baseDomain) || string.Equals(host, baseDomain, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var suffix = "." + baseDomain;
        if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var prefix = host[..^suffix.Length];
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return null;
        }

        return prefix.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim().ToLowerInvariant();
    }

}
