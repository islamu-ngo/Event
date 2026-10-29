using System.Net;
using System.Security.Claims;
using Event.Testing.DataProtection;
using Explore.Blazor.Extensions;
using Explore.Blazor.IntegrationTests.Fixtures;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class BffDataProtectionCookieRestartTests
{
    private const string CookieName = ".AspNetCore.RestartProof";

    [Test]
    public async Task DataProtectionRejectsMissingAuthorityInsteadOfUsingLocalKeys()
    {
        var services = new ServiceCollection();
        services.AddBffDataProtection();
        services.AddSingleton<IExternalDataProtectionSource>(new MissingAuthority());
        await using var provider = services.BuildServiceProvider();
        await Assert.That(() => provider.GetRequiredService<IDataProtectionProvider>())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CookieTicketSurvivesFreshBffHostWhenKeyRingPersists()
    {
        var authority = new ExternalDataProtectionTestAuthority("bff");
        var sessions = new TestBffSessionAuthority();

        string cookieHeader;

        await using (var firstHost = await StartCookieHostAsync(authority, sessions))
        {
            using var firstClient = firstHost.GetTestClient();
            using var loginResponse = await firstClient.GetAsync("/login-test");

            await Assert.That(loginResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
            cookieHeader = GetCookieHeader(loginResponse);
        }

        await using (var secondHost = await StartCookieHostAsync(authority, sessions))
        {
            using var secondClient = secondHost.GetTestClient();
            secondClient.DefaultRequestHeaders.Add("Cookie", cookieHeader);

            using var authResponse = await secondClient.GetAsync("/auth-required");
            var body = await authResponse.Content.ReadAsStringAsync();

            await Assert.That(authResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(body).Contains("restart-user");
        }
    }

    private static async Task<WebApplication> StartCookieHostAsync(IExternalDataProtectionSource authority,
        TestBffSessionAuthority sessions)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });

        builder.WebHost.UseTestServer();
        builder.Services.AddBffDataProtection();
        builder.Services.AddSingleton(authority);
        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.Cookie.Name = CookieName;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
            });
        builder.Services.AddAuthorization();
        builder.Services.AddTestBffSessionAuthority(sessions);

        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/login-test", async context =>
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "restart-user"),
                    new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())],
                CookieAuthenticationDefaults.AuthenticationScheme);

            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));
        });

        app.MapGet("/auth-required", (ClaimsPrincipal user) =>
                Results.Ok(user.Identity?.Name ?? string.Empty))
            .RequireAuthorization();

        await app.StartAsync();
        return app;
    }

    private static string GetCookieHeader(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            throw new InvalidOperationException("Login response did not set the BFF auth cookie.");
        }

        var cookie = values.FirstOrDefault(value => value.StartsWith(CookieName, StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(cookie))
        {
            throw new InvalidOperationException("Login response did not include the expected BFF auth cookie.");
        }

        return cookie.Split(';', 2)[0];
    }

    private sealed class MissingAuthority : IExternalDataProtectionSource
    {
        public ValueTask PrepareAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new InvalidOperationException("authority_missing"));
        public ExternalDataProtectionInput Read() => throw new InvalidOperationException("authority_missing");
        public ValueTask<ExternalDataProtectionInput> ReadFreshAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ExternalDataProtectionInput>(new InvalidOperationException("authority_missing"));
    }
}
