// ABOUTME: Completes split-phase tenant handling after authentication for API-key callers and mismatch checks.
// ABOUTME: Sets tenant context from authenticated machine principals and fail-closes when tenant hints conflict.

using Explore.API.Authentication;
using Explore.API.Configuration;
using Explore.API.ExceptionHandling;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Services;
using Explore.Application.Telemetry;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Explore.API.Middleware;

public sealed class ApiTenantPostAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiTenantPostAuthenticationMiddleware> _logger;

    public ApiTenantPostAuthenticationMiddleware(RequestDelegate next, ILogger<ApiTenantPostAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContextAccessor tenantContextAccessor,
        IProblemDetailsService problemDetailsService,
        BusinessMetrics metrics,
        IOptions<McpAdapterSettings> mcpAdapterOptions)
    {
        var isMcpPath = IsEnabledMcpPath(context, mcpAdapterOptions.Value);
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) && !isMcpPath)
        {
            await _next(context);
            return;
        }

        if (AtprotoTransientAuthenticationDefaults.IsPrivatePath(context.Request.Path)
            && context.User.Identity is { IsAuthenticated: true, AuthenticationType: AtprotoTransientAuthenticationDefaults.Scheme })
        {
            // The machine can operate only on instance-owned transient infrastructure, not as a tenant or user.
            await _next(context);
            return;
        }

        var apiKeyPrincipal = context.User.TryGetApiKeyPrincipalContext();
        var authenticatedTenantId = apiKeyPrincipal?.TenantId;
        var requestedTenantId = ResolveRequestedTenantId(context);
        var hasApiKeyHeader = ApiKeyHeaderReader.HasNonEmptyApiKey(context.Request);

        if (authenticatedTenantId is Guid authenticatedApiKeyTenantId &&
            requestedTenantId is Guid hintedTenantId &&
            authenticatedApiKeyTenantId != Guid.Empty &&
            hintedTenantId != Guid.Empty &&
            authenticatedApiKeyTenantId != hintedTenantId)
        {
            metrics.RecordExternalApiKeyAuthentication(
                "tenant_mismatch",
                authenticatedApiKeyTenantId.ToString(),
                apiKeyPrincipal?.OwnerType.ToString());
            _logger.LogWarning(
                "External API key {KeyId} for tenant {AuthenticatedTenantId} attempted mismatched tenant {RequestedTenantId} on {Path}.",
                apiKeyPrincipal?.KeyId,
                authenticatedApiKeyTenantId,
                hintedTenantId,
                context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Tenant mismatch",
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                    Detail = "The authenticated tenant does not match the requested tenant context.",
                    Instance = context.Request.Path
                }
            });
            return;
        }

        if (!tenantContextAccessor.IsResolved)
        {
            if (authenticatedTenantId is Guid principalTenantId && principalTenantId != Guid.Empty)
            {
                tenantContextAccessor.SetTenant(principalTenantId);
                await _next(context);
                return;
            }

            if (hasApiKeyHeader && !isMcpPath)
            {
                if (apiKeyPrincipal?.OwnerType == ExternalApiKeyOwnerType.InstanceAdmin)
                {
                    if (requestedTenantId is Guid requestedInstanceAdminTenantId && requestedInstanceAdminTenantId != Guid.Empty)
                    {
                        tenantContextAccessor.SetTenant(requestedInstanceAdminTenantId);
                        if (_logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.LogInformation(
                                "InstanceAdmin external API key {KeyId} bound to requested tenant context for {Path}.",
                                apiKeyPrincipal.KeyId,
                                context.Request.Path);
                        }

                        await _next(context);
                        return;
                    }

                    if (IsHostAdministrationPath(context.Request.Path))
                    {
                        if (_logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.LogInformation(
                                "InstanceAdmin external API key {KeyId} continued without tenant context for host-administration path {Path}.",
                                apiKeyPrincipal.KeyId,
                                context.Request.Path);
                        }

                        await _next(context);
                        return;
                    }

                    metrics.RecordExternalApiKeyAuthentication(
                        "tenant_required",
                        "platform",
                        apiKeyPrincipal.OwnerType.ToString());
                    _logger.LogWarning(
                        "InstanceAdmin external API key {KeyId} attempted tenant-scoped API path {Path} without an explicit tenant context.",
                        apiKeyPrincipal.KeyId,
                        context.Request.Path);
                    context.Response.StatusCode = StatusCodes.Status404NotFound;

                    await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = context,
                        ProblemDetails = new ProblemDetails
                        {
                            Status = StatusCodes.Status404NotFound,
                            Title = "Tenant not resolved",
                            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                            Detail = "The tenant could not be resolved for this request.",
                            Instance = context.Request.Path,
                            Extensions =
                            {
                                ["code"] = ApiProblemCodes.TenantRequired
                            }
                        }
                    });
                    return;
                }

                if (apiKeyPrincipal is not null &&
                    requestedTenantId is Guid requestedTenantIdForAuthenticatedKey &&
                    requestedTenantIdForAuthenticatedKey != Guid.Empty)
                {
                    metrics.RecordExternalApiKeyAuthentication(
                        "tenant_mismatch",
                        requestedTenantIdForAuthenticatedKey.ToString(),
                        apiKeyPrincipal?.OwnerType.ToString());
                    _logger.LogWarning(
                        "External API key {KeyId} without a tenant claim attempted requested tenant {RequestedTenantId} on {Path}.",
                        apiKeyPrincipal?.KeyId,
                        requestedTenantIdForAuthenticatedKey,
                        context.Request.Path);
                    context.Response.StatusCode = StatusCodes.Status404NotFound;

                    await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = context,
                        ProblemDetails = new ProblemDetails
                        {
                            Status = StatusCodes.Status404NotFound,
                            Title = "Tenant mismatch",
                            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                            Detail = "The authenticated tenant does not match the requested tenant context.",
                            Instance = context.Request.Path
                        }
                    });
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;

                await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status401Unauthorized,
                        Title = "API key authentication failed",
                        Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
                        Detail = "The API key could not be authenticated for this request.",
                        Instance = context.Request.Path
                    }
                });
                return;
            }

            if (hasApiKeyHeader && isMcpPath)
            {
                if (apiKeyPrincipal?.OwnerType == ExternalApiKeyOwnerType.InstanceAdmin)
                {
                    metrics.RecordExternalApiKeyAuthentication(
                        "tenant_required",
                        "platform",
                        apiKeyPrincipal.OwnerType.ToString());
                    _logger.LogWarning(
                        "InstanceAdmin external API key {KeyId} attempted MCP path {Path} without an explicit tenant context.",
                        apiKeyPrincipal.KeyId,
                        context.Request.Path);
                }

                context.Response.StatusCode = StatusCodes.Status404NotFound;

                await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status404NotFound,
                        Title = "Tenant not resolved",
                        Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                        Detail = "The tenant could not be resolved for this request.",
                        Instance = context.Request.Path,
                        Extensions =
                        {
                            ["code"] = ApiProblemCodes.TenantRequired
                        }
                    }
                });
                return;
            }

            await _next(context);
            return;
        }

        if (authenticatedTenantId is Guid resolvedPrincipalTenantId &&
            tenantContextAccessor.TenantId is Guid resolvedRequestTenantId &&
            resolvedPrincipalTenantId != Guid.Empty &&
            resolvedRequestTenantId != Guid.Empty &&
            resolvedPrincipalTenantId != resolvedRequestTenantId)
        {
            metrics.RecordExternalApiKeyAuthentication(
                "tenant_mismatch",
                resolvedPrincipalTenantId.ToString(),
                apiKeyPrincipal?.OwnerType.ToString());
            _logger.LogWarning(
                "External API key {KeyId} for tenant {AuthenticatedTenantId} conflicted with resolved request tenant {ResolvedTenantId} on {Path}.",
                apiKeyPrincipal?.KeyId,
                resolvedPrincipalTenantId,
                resolvedRequestTenantId,
                context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Tenant mismatch",
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                    Detail = "The authenticated tenant does not match the requested tenant context.",
                    Instance = context.Request.Path
                }
            });
            return;
        }

        await _next(context);
    }

    private static Guid? ResolveRequestedTenantId(HttpContext context)
    {
        return context.Items.TryGetValue(ApiTenantResolutionMiddleware.RequestedTenantIdItemKey, out var value) &&
               value is Guid tenantId &&
               tenantId != Guid.Empty
            ? tenantId
            : null;
    }

    private static bool IsEnabledMcpPath(HttpContext context, McpAdapterSettings settings)
    {
        return settings.Enabled &&
               !string.IsNullOrWhiteSpace(settings.EndpointPath) &&
               context.Request.Path.StartsWithSegments(
                   settings.EndpointPath,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHostAdministrationPath(PathString path)
    {
        return path.StartsWithSegments("/api/InstanceOnboarding", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/System", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/instance/settings", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/managed-provider-provisioning", StringComparison.OrdinalIgnoreCase);
    }
}
