using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Event.Standalone.IntegrationTests.Fixtures;
using Event.Web.BffHosting.Security;
using Explore.Blazor.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Standalone.IntegrationTests;

/// <summary>
/// Combined-host bridge evidence against the actual native API route. Successful native resource
/// authorization and byte production remain covered by Event.API integration tests.
/// </summary>
[NotInParallel]
public sealed partial class CombinedEventResourceTransportTests
{
    [Test]
    public async Task ForgedBrowserAuthorityCannotExposeNativeResourceDelivery()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        var (cookie, _) = await CreateSessionCookieAsync(factory);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/eventresource/{Guid.CreateVersion7():D}/content");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "browser-forged");
        request.Headers.Add(EventBffHeaderNames.TenantSlug, "browser-forged");

        using var response = await client.SendAsync(request);
        byte[] body = await response.Content.ReadAsByteArrayAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(body).DoesNotContain((byte)'%');
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    public async Task UntrustedBrowserSessionCannotReachNativeResourceUpload()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/eventresource/{Guid.CreateVersion7():D}/upload-sessions")
        {
            Content = JsonContent.Create(new
            {
                expectedVersion = Guid.CreateVersion7(),
                expectedSizeBytes = 4,
                contentType = "application/pdf",
                safeDisplayName = "handout.pdf",
                extension = "pdf",
                idempotencyKey = Guid.CreateVersion7().ToString("N")
            })
        };
        var (cookie, principal) = await CreateSessionCookieAsync(factory);
        using var csrfScope = factory.Services.CreateScope();
        var csrf = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            RequestServices = csrfScope.ServiceProvider,
            User = principal
        };
        csrf.Request.Scheme = "https";
        var tokens = csrfScope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>()
            .GetAndStoreTokens(csrf);
        string[] issued = csrf.Response.Headers.SetCookie.Select(value => value!.Split(';', 2)[0]).ToArray();
        request.Headers.Add("Cookie", string.Join("; ", issued.Prepend(cookie)));
        request.Headers.Add("X-CSRF-TOKEN", tokens.RequestToken);
        request.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(await response.Content.ReadAsStringAsync()).DoesNotContain("provider");
    }

    private static async Task<(string Cookie, ClaimsPrincipal Principal)> CreateSessionCookieAsync(
        StandaloneWebApplicationFactory factory)
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        string token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            expires: DateTime.UtcNow.AddMinutes(10)));
        var properties = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = token }]);
        Guid userId = Guid.CreateVersion7();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", userId.ToString("D")),
                new Claim(ClaimTypes.NameIdentifier, userId.ToString("D"))
            ],
            CookieAuthenticationDefaults.AuthenticationScheme));
        string cookie = await TestBffSessionAuthorityRegistration.IssueFreshCookieAsync(factory.Services, new AuthenticationTicket(
            principal,
            properties,
            CookieAuthenticationDefaults.AuthenticationScheme));
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = factory.Services };
        context.Request.Headers.Cookie = cookie;
        await Assert.That(await options.ReadStoredTicketAsync(context)).IsNotNull();
        return (cookie, principal);
    }
}
