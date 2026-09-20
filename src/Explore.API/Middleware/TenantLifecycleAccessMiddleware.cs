using Explore.API.Attributes;
using Explore.API.Configuration;
using Explore.API.Controllers;
using Explore.Application.Contracts.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;

namespace Explore.API.Middleware;

/// <summary>Fresh lifecycle enforcement after trusted binding, before response replay and output caching.</summary>
public sealed class TenantLifecycleAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContextAccessor tenantContext,
        ITenantLifecycleAccessService lifecycle, IProblemDetailsService problems,
        IOptions<McpAdapterSettings> mcpOptions)
    {
        var mcp = mcpOptions.Value;
        bool tenantSurface = context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            || mcp.Enabled && !string.IsNullOrWhiteSpace(mcp.EndpointPath)
                && context.Request.Path.StartsWithSegments(mcp.EndpointPath, StringComparison.OrdinalIgnoreCase);
        if (!tenantSurface || ApiTenantResolutionMiddleware.IsTenantExemptPath(context.Request.Path)
            || IsExistingAuthenticationOrSignedCallback(context.GetEndpoint())
            || HttpMethods.IsGet(context.Request.Method)
                && context.Request.Path.Equals(new PathString("/api/instance/settings/branding"), StringComparison.OrdinalIgnoreCase)
                && context.User.Identities.Any(identity => identity.IsAuthenticated
                    && identity.AuthenticationType == Explore.Application.Constants.ApiAuthenticationSchemeNames.SetupSecret))
        {
            await next(context);
            return;
        }

        bool management = context.GetEndpoint()?.Metadata.GetMetadata<TenantLifecycleManagementAttribute>() is not null;
        bool allowed = management
            ? context.User.Identity?.IsAuthenticated == true
                && await lifecycle.CanManageAsync(tenantContext.TenantId, context.RequestAborted)
            : await lifecycle.IsPublicAsync(tenantContext.TenantId, context.RequestAborted);
        if (allowed)
        {
            if (management)
                context.Response.Headers.CacheControl = "no-store";
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.Headers.CacheControl = "no-store";
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "tenant_lifecycle_unavailable",
                Extensions = { ["code"] = "tenant_lifecycle_unavailable" }
            }
        });
    }

    private static bool IsExistingAuthenticationOrSignedCallback(Endpoint? endpoint)
    {
        var action = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
        return action is not null
            && (action.ControllerTypeInfo.AsType() == typeof(LocalAuthController)
                && action.MethodInfo.Name == nameof(LocalAuthController.Login)
                || action.ControllerTypeInfo.AsType() == typeof(IncomingWebhooksController)
                && action.MethodInfo.Name is nameof(IncomingWebhooksController.RecordStripeConnectCallback)
                    or nameof(IncomingWebhooksController.RecordSvixOperationalCallback));
    }
}
