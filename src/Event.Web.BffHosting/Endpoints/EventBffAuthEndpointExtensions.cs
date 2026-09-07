using Event.Web.BffHosting.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Event.Web.BffHosting.Endpoints;

public static class EventBffAuthEndpointExtensions
{
    public static WebApplication MapEventBffAuthEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/auth/login", HandleLogin);
        app.MapGet("/auth/challenge", HandleChallengeAsync);
        app.MapGet("/auth/signout", HandleSignoutAsync)
            .RequireAuthorization();
        app.MapGet("/auth/status", HandleStatus);
        app.MapGet("/forbidden", HandleForbidden);

        return app;
    }

    private static IResult HandleLogin(HttpContext context)
    {
        var returnUrl = ResolveSafeReturnUrl(context);
        var encodedReturnUrl = Uri.EscapeDataString(returnUrl);
        return Results.Redirect($"/auth/challenge?returnUrl={encodedReturnUrl}");
    }

    private static async Task HandleChallengeAsync(HttpContext context)
    {
        var returnUrl = ResolveSafeReturnUrl(context);
        await context.ChallengeAsync(
            EventBffAuthenticationSchemes.Keycloak,
            new AuthenticationProperties { RedirectUri = returnUrl });
    }

    private static async Task HandleSignoutAsync(HttpContext context)
    {
        var returnUrl = ResolveSafeReturnUrl(context);
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignOutAsync(
            EventBffAuthenticationSchemes.Keycloak,
            new AuthenticationProperties { RedirectUri = returnUrl });
    }

    private static IResult HandleStatus(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store, no-cache";
        context.Response.Headers.Pragma = "no-cache";

        if (context.User.Identity?.IsAuthenticated == true)
        {
            return Results.Ok(new
            {
                isAuthenticated = true,
                name = context.User.Identity.Name
            });
        }

        return Results.Ok(new { isAuthenticated = false });
    }

    private static IResult HandleForbidden() =>
        Results.Problem(
            title: "Access denied",
            detail: "This control-plane surface requires instance administrator authority.",
            statusCode: StatusCodes.Status403Forbidden);

    private static string ResolveSafeReturnUrl(HttpContext context)
    {
        var returnUrl = context.Request.Query["returnUrl"].ToString();
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        if (!Uri.TryCreate(returnUrl, UriKind.Relative, out _))
        {
            return "/";
        }

        return returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? "/"
            : returnUrl;
    }
}
