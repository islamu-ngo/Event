using Explore.API.Authentication;
using Explore.API.Attributes;
using Explore.API.Configuration;
using Explore.Application.Constants;
using Explore.Application.Contracts.Services;
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

    /// <summary>
    /// Resolves trusted tenant context before forwarding tenant surfaces, while leaving exact global management routes to owner authorization.
    /// </summary>
    public async Task InvokeAsync(
        HttpContext context,
        ITenantContextAccessor tenantContextAccessor,
        IProblemDetailsService problemDetailsService,
        IDeploymentModeProvider deploymentModeProvider,
        IOptions<McpAdapterSettings> mcpAdapterOptions,
        IServiceScopeFactory scopeFactory)
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

        bool privateSessionRead = TenantLifecycleAccessMiddleware.IsPrivateAdministratorSessionRead(context.GetEndpoint());
        string? requestedSlug = context.Request.Headers[TenantHeaderNames.TenantSlug].FirstOrDefault();
        bool explicitlySelectedSessionTenant = privateSessionRead && !string.IsNullOrWhiteSpace(requestedSlug);
        var hasApiKeyHeader = ApiKeyHeaderReader.HasNonEmptyApiKey(context.Request);
        Guid? resolvedTenantId;
        await using (var routingScope = scopeFactory.CreateAsyncScope())
        {
            var tenantSlugCache = routingScope.ServiceProvider.GetRequiredService<ITenantSlugCache>();
            resolvedTenantId = await ResolveFromSlugHeaderAsync(context, tenantSlugCache);
            if (!explicitlySelectedSessionTenant && resolvedTenantId is null
                && context.Items.TryGetValue(EffectiveHostAuthorityMiddleware.TenantItemKey, out var hostTenant)
                && hostTenant is Guid verifiedTenant)
                resolvedTenantId = verifiedTenant;

            // The public routing cache excludes unpublished tenants. This directory binding grants no authority.
            if (resolvedTenantId is null && !hasApiKeyHeader && !string.IsNullOrWhiteSpace(requestedSlug)
                && (TenantLifecycleAccessMiddleware.IsUserSynchronization(context.GetEndpoint())
                    || TenantLifecycleAccessMiddleware.IsPrivateAdministratorSessionRead(context.GetEndpoint())))
            {
                var lifecycle = routingScope.ServiceProvider.GetRequiredService<ITenantLifecycleAccessService>();
                resolvedTenantId = await lifecycle.ResolveConfiguredAdministratorTenantAsync(requestedSlug, context.RequestAborted);
            }
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

        // Native key management checks owner authority and supports global keys.
        // Preserve any tenant resolution above rather than inventing a scope.
        if (IsApiKeyManagementRequest(context.Request))
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

    /// <summary>
    /// Recognizes only collection GET/POST and GUID DELETE paths; unresolved tenancy does not exempt unrelated API-key routes.
    /// </summary>
    internal static bool IsApiKeyManagementRequest(HttpRequest request)
    {
        if (request.Path.Equals(new PathString("/api/externalapikey"), StringComparison.OrdinalIgnoreCase))
            return HttpMethods.IsPost(request.Method) || HttpMethods.IsGet(request.Method);

        return HttpMethods.IsDelete(request.Method)
            && request.Path.StartsWithSegments("/api/externalapikey", StringComparison.OrdinalIgnoreCase, out var remainder)
            && remainder.Value is { Length: > 1 } path
            && Guid.TryParse(path.AsSpan(1), out _);
    }

    private static bool IsEnabledMcpPath(HttpContext context, McpAdapterSettings settings)
    {
        return settings.Enabled &&
               !string.IsNullOrWhiteSpace(settings.EndpointPath) &&
               context.Request.Path.StartsWithSegments(
                   settings.EndpointPath,
                   StringComparison.OrdinalIgnoreCase);
    }

}
