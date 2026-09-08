using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace Explore.API.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PrivateNoStoreAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        Apply(context.HttpContext);
    }

    public override void OnResultExecuting(ResultExecutingContext context)
    {
        Apply(context.HttpContext);
    }

    private static void Apply(HttpContext context)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "private, no-store";
        context.Response.Headers[HeaderNames.Pragma] = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
