using System.Net;
using System.Security.Cryptography;
using Event.Testing.DataProtection;
using Explore.Blazor.Extensions;
using Explore.Blazor.IntegrationTests.Services;
using Explore.Blazor.Services.Auth;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Explore.Blazor.IntegrationTests.Endpoints;

[ClassDataSource<RedisBffTicketStoreTests.Authority>(Shared = SharedType.PerClass)]
public sealed class BffNativeSessionStoreLifecycleTests(RedisBffTicketStoreTests.Authority authority)
{
    [Test]
    public async Task ValidRefreshRetainsSessionAndLogoutRevokesUnchangedCookie()
    {
        using var store = authority.Create();
        await using var app = await Host(store, authority.Source);
        using var client = app.GetTestClient();
        using var login = await client.GetAsync("/login");
        client.DefaultRequestHeaders.Add("Cookie", Cookie(login));
        using var before = await client.GetAsync("/who");
        await Assert.That(before.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var refresh = await client.GetAsync("/refresh-retained");
        await Assert.That(refresh.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var after = await client.GetAsync("/who");
        await Assert.That(after.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var logout = await client.GetAsync("/logout");
        using var replay = await client.GetAsync("/who");
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task RetainedRevokedTicketCannotSignInAsFreshSession()
    {
        using var store = authority.Create();
        await using var app = await Host(store, authority.Source);
        using var client = app.GetTestClient();
        using var login = await client.GetAsync("/login");
        client.DefaultRequestHeaders.Add("Cookie", Cookie(login));
        using var capture = await client.GetAsync("/capture");
        await Assert.That(capture.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var logout = await client.GetAsync("/logout");
        using var refresh = await client.GetAsync("/refresh-captured");
        await Assert.That(refresh.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(refresh.Headers.Contains("Set-Cookie")).IsFalse();
        using var replay = await client.GetAsync("/who");
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task SameRequestLogoutAndFreshSignInCannotResurrectOriginalCookie()
    {
        using var store = authority.Create();
        await using var app = await Host(store, authority.Source);
        using var client = app.GetTestClient();
        using var login = await client.GetAsync("/login");
        string oldCookie = Cookie(login);
        client.DefaultRequestHeaders.Add("Cookie", oldCookie);
        using var replacement = await client.GetAsync("/replace");
        await Assert.That(replacement.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        using var replay = await client.GetAsync("/who");
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    internal static async Task<WebApplication> Host(ITicketStore store, IExternalDataProtectionSource? source = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddBffDataProtection();
        builder.Services.AddSingleton<IExternalDataProtectionSource>(source ?? new ExternalDataProtectionTestAuthority("bff"));
        builder.Services.AddAuthentication("Cookies").AddCookie("Cookies", options =>
        {
            options.SessionStore = store;
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
        });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            try { await next(context); }
            catch (BffSessionAuthorityUnavailableException)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            }
        });
        app.UseAuthentication();
        AuthenticationTicket? captured = null;
        app.MapGet("/login", context =>
        {
            var properties = new AuthenticationProperties();
            properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = Convert.ToHexString(RandomNumberGenerator.GetBytes(48)) }]);
            return context.SignInAsync("Cookies", Principal(), properties);
        });
        app.MapGet("/capture", async context =>
        {
            captured = (await context.AuthenticateAsync("Cookies")).Ticket;
            context.Response.StatusCode = captured is null ? StatusCodes.Status401Unauthorized : StatusCodes.Status200OK;
        });
        app.MapGet("/refresh-captured", async context =>
        {
            if (captured is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            await context.SignInAsync("Cookies", captured.Principal, captured.Properties);
        });
        app.MapGet("/refresh-retained", async context =>
        {
            var authentication = await context.AuthenticateAsync("Cookies");
            if (!authentication.Succeeded)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            await context.SignInAsync("Cookies", authentication.Principal!, authentication.Properties);
        });
        app.MapGet("/replace", async context =>
        {
            await context.SignOutAsync("Cookies");
            await context.SignInAsync("Cookies", Principal());
        });
        app.MapGet("/logout", context => context.SignOutAsync("Cookies"));
        app.MapGet("/who", (HttpContext context) => context.User.Identity?.IsAuthenticated == true
            ? Results.Ok() : Results.Unauthorized());
        await app.StartAsync();
        return app;
    }

    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity([new Claim("sub", "native-session-owner")], "Cookies"));
    internal static string Cookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Last(value => value.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal)).Split(';')[0];

}
