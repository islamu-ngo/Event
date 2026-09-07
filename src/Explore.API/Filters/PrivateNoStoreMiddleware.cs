using Microsoft.Net.Http.Headers;

namespace Explore.API.Filters;

public sealed class PrivateNoStoreMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<PrivateNoStoreAttribute>() is not null)
        {
            Apply(context);
            context.Response.OnStarting(
                static state =>
                {
                    Apply((HttpContext)state);
                    return Task.CompletedTask;
                },
                context);
        }

        await next(context);
    }

    private static void Apply(HttpContext context)
    {
        context.Response.Headers[HeaderNames.CacheControl] =
            "private, no-store";
        context.Response.Headers[HeaderNames.Pragma] = "no-cache";
        context.Response.Headers["Referrer-Policy"] =
            "no-referrer";
    }
}
