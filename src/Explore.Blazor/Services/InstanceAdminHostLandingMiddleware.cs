using Event.Web.BffHosting.Abstractions;

namespace Explore.Blazor.Services;

public sealed class InstanceAdminHostLandingMiddleware(
    RequestDelegate next,
    IEventBffHostClassifier hostClassifier)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path == "/" && hostClassifier.IsAdminHost(context))
        {
            context.Response.Redirect(context.Request.PathBase + "/settings/instance");
            return Task.CompletedTask;
        }

        return next(context);
    }
}
