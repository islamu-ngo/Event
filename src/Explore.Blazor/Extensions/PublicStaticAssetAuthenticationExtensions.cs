using System.Security.Claims;
using Explore.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Net.Http.Headers;

namespace Explore.Blazor.Extensions;

public static class PublicStaticAssetAuthenticationExtensions
{
    /// <summary>Runs explicitly public manifest assets without interpreting browser credentials.</summary>
    public static IApplicationBuilder UsePublicStaticAssetAuthenticationBoundary(this IApplicationBuilder app) =>
        app.Use(InvokePublicStaticAssetAuthenticationBoundaryAsync);

    private static async Task InvokePublicStaticAssetAuthenticationBoundaryAsync(HttpContext context, RequestDelegate next)
    {
        var endpoint = context.GetEndpoint();
        if (!(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            || endpoint?.Metadata.GetMetadata<PublicStaticAssetMetadata>() is null
            || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
        {
            await next(context);
            return;
        }

        var cookie = context.Request.Headers.Cookie;
        var authorization = context.Request.Headers.Authorization;
        var principal = context.User;
        context.Request.Headers.Remove(HeaderNames.Cookie);
        context.Request.Headers.Remove(HeaderNames.Authorization);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        try
        {
            await next(context);
        }
        finally
        {
            context.Request.Headers.Cookie = cookie;
            context.Request.Headers.Authorization = authorization;
            context.User = principal;
        }
    }
}
