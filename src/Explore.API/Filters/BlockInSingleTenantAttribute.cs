using Explore.Application.Contracts.Services;
using Explore.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Explore.API.Filters;

/// <summary>
/// Authorization filter that blocks access to endpoints in single-tenant mode.
/// Apply to controllers or actions that should only be available in multi-tenant deployments.
/// Returns 404 (Not Found) to hide the endpoint from discovery.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// [BlockInSingleTenant]
/// [ApiController]
/// public class TenantAdminController : ControllerBase { }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class BlockInSingleTenantAttribute : Attribute, IAsyncAuthorizationFilter
{
    /// <summary>
    /// Called early in the filter pipeline to confirm request is authorized.
    /// In single-tenant mode with HidePlatformAdminInSingleTenant enabled, returns 404.
    /// </summary>
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var deploymentSettings = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<DeploymentSettings>>().Value;

        if (!deploymentSettings.HidePlatformAdminInSingleTenant)
            return;

        var provider = context.HttpContext.RequestServices
            .GetRequiredService<IDeploymentModeProvider>();

        if (await provider.IsSingleTenantAsync(context.HttpContext.RequestAborted))
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Title = "Endpoint unavailable in single-tenant mode",
                Status = StatusCodes.Status404NotFound,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4"
            })
            {
                StatusCode = StatusCodes.Status404NotFound
            };
        }
    }
}

/// <summary>
/// Authorization filter that requires multi-tenant mode.
/// Unlike BlockInSingleTenant, this returns 403 Forbidden with an error message.
/// Use when you want to inform the client that the feature is unavailable.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class RequireMultiTenantAttribute : Attribute, IAsyncAuthorizationFilter
{
    /// <summary>
    /// Called early in the filter pipeline to confirm request is authorized.
    /// In single-tenant mode, returns 403 Forbidden with explanation.
    /// </summary>
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var provider = context.HttpContext.RequestServices
            .GetRequiredService<IDeploymentModeProvider>();

        if (await provider.IsSingleTenantAsync(context.HttpContext.RequestAborted))
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Title = "Multi-tenant required",
                Detail = "This endpoint is only available in multi-tenant deployments.",
                Status = StatusCodes.Status403Forbidden,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.3"
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
