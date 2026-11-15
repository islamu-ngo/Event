using Explore.API.Attributes;
using Explore.API.Configuration;
using Explore.Application.Contracts.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Explore.API.Middleware;

/// <summary>Admits tenant reads before idempotency, output-cache and conditional-response replay.</summary>
public sealed class TenantVisitorReadAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContextAccessor tenantContext,
        IVisitorTenantReadAccessPolicy policy, IProblemDetailsService problems,
        IOptions<McpAdapterSettings> mcpOptions)
    {
        var endpoint = context.GetEndpoint();
        bool tenantSurface = ApiTenantResolutionMiddleware.IsTenantSurface(context, mcpOptions.Value);
        if (!tenantSurface
            || endpoint?.Metadata.GetMetadata<TenantReadBootstrapAttribute>() is not null
            || endpoint?.Metadata.GetMetadata<TenantLifecycleManagementAttribute>() is not null
            || endpoint?.Metadata.GetMetadata<InstanceManagementAttribute>() is not null)
        {
            await next(context);
            return;
        }

        Guid? tenantId = tenantContext.IsResolved ? tenantContext.TenantId : null;
        var decision = endpoint?.Metadata.GetMetadata<TenantMachinePublicReadAttribute>() is not null
            ? await policy.CheckPublicAsync(tenantId, context.RequestAborted)
            : await policy.CheckAsync(tenantId, context.RequestAborted);
        if (decision == VisitorTenantReadAccessDecision.Allowed)
        {
            await next(context);
            return;
        }

        var (status, code) = decision switch
        {
            VisitorTenantReadAccessDecision.AuthenticationRequired =>
                (StatusCodes.Status401Unauthorized, "tenant_authentication_required"),
            VisitorTenantReadAccessDecision.Unavailable =>
                (StatusCodes.Status503ServiceUnavailable, "tenant_read_policy_unavailable"),
            VisitorTenantReadAccessDecision.NotFound =>
                (StatusCodes.Status404NotFound, "tenant_not_found"),
            _ => throw new InvalidOperationException("The tenant read decision is invalid.")
        };
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        if (status == StatusCodes.Status401Unauthorized)
            context.Response.Headers.WWWAuthenticate = "Bearer";
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = code,
                Extensions = { ["code"] = code }
            }
        });
    }
}
