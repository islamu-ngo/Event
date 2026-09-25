using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Event.Standalone.IntegrationTests.Fixtures;
using Event.Web.BffHosting.Security;
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
        string cookie = CreateSessionCookie(factory, out _);
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
        string cookie = CreateSessionCookie(factory, out var principal);
        using var csrfScope = factory.Services.CreateScope();
        var csrf = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            RequestServices = csrfScope.ServiceProvider, User = principal
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

    private static string CreateSessionCookie(StandaloneWebApplicationFactory factory, out ClaimsPrincipal principal)
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        string token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            expires: DateTime.UtcNow.AddMinutes(10)));
        var properties = new AuthenticationProperties();
        properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = token }]);
        Guid userId = Guid.CreateVersion7();
        principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", userId.ToString("D")),
                new Claim(ClaimTypes.NameIdentifier, userId.ToString("D"))
            ],
            CookieAuthenticationDefaults.AuthenticationScheme));
        string protectedTicket = options.TicketDataFormat.Protect(new AuthenticationTicket(
            principal,
            properties,
            CookieAuthenticationDefaults.AuthenticationScheme));
        return $"{options.Cookie.Name}={Uri.EscapeDataString(protectedTicket)}";
    }
}
