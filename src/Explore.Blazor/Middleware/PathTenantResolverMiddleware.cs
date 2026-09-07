using Explore.Blazor.Services;

namespace Explore.Blazor.Middleware;

public class PathTenantResolverMiddleware
{
    private readonly RequestDelegate _next;

    public PathTenantResolverMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IBffResolverConfigurationProvider resolverConfigurationProvider,
        ITenantRouteContextAccessor tenantRouteContextAccessor)
    {
        var configuration = await resolverConfigurationProvider.GetConfigurationAsync(context.RequestAborted);
        if (configuration.PathEnabled != true)
        {
            await _next(context);
            return;
        }

        if (!TenantRoutePathMatcher.TryMatch(
                context.Request.Path,
                configuration.PathPrefix,
                out var tenantSlug,
                out var matchedPathBase,
                out var rewrittenPath))
        {
            await _next(context);
            return;
        }

        tenantRouteContextAccessor.SetTenantSlug(tenantSlug);

        context.Request.PathBase = context.Request.PathBase.Add(matchedPathBase);
        context.Request.Path = rewrittenPath.HasValue ? rewrittenPath : new PathString("/");

        await _next(context);
    }
}
