// ABOUTME: Exercises Local Identity BFF antiforgery and HttpOnly cookie session behavior.
// ABOUTME: Proves access tokens stay server-side while successful login establishes browser authentication.

using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.IntegrationTests.Fixtures;
using Explore.Blazor.Models;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class LocalBffAuthenticationFlowTests : IAsyncDisposable
{
    private readonly string _accessToken = CreateAccessToken();
    private readonly BlazorBffWebApplicationFactory _rootFactory = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public LocalBffAuthenticationFlowTests()
    {
        _factory = _rootFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILocalAuthClient>();
                services.AddSingleton<ILocalAuthClient>(
                    new LocalAuthClientStub(_accessToken));
                services.RemoveAll<IBffOnboardingStatusProvider>();
                services.AddSingleton<IBffOnboardingStatusProvider>(
                    new CompletedOnboardingStatusProvider());
                services.RemoveAll<IDynamicAuthSchemeManager>();
                services.AddSingleton<IDynamicAuthSchemeManager>(
                    new LocalPrimarySchemeManager());
                services.AddScoped<BffAdminClaimsTransformation>();
            });
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    [Test]
    public async Task FormerRegistrationRouteIsAbsentEvenWithValidAntiforgery()
    {
        const string formerRoute = "/bff/auth/local/register";
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>().ToArray();
        await Assert.That(endpoints.Where(endpoint => string.Equals(
            endpoint.RoutePattern.RawText, formerRoute, StringComparison.OrdinalIgnoreCase)).ToArray())
            .IsEmpty()
            .Because("The registration operation must be removed from the native endpoint graph.");

        string antiforgeryToken = await IssueAntiforgeryCookieAsync();
        var credentials = CreateLoginRequest();
        var registrationBody = new
        {
            credentials.Email,
            credentials.Password,
            FirstName = "Amina",
            LastName = "Noor",
            credentials.IsPersistent,
            credentials.ReturnUrl
        };
        using var request = new HttpRequestMessage(
            method: HttpMethod.Post,
            requestUri: formerRoute)
        {
            Content = JsonContent.Create(registrationBody)
        };
        request.Headers.Add("X-CSRF-TOKEN", antiforgeryToken);
        using var unknownRequest = new HttpRequestMessage(
            method: HttpMethod.Post,
            requestUri: "/bff/auth/local/unknown-route-contract-check")
        {
            Content = JsonContent.Create(registrationBody)
        };
        unknownRequest.Headers.Add("X-CSRF-TOKEN", antiforgeryToken);

        using var response = await _client.SendAsync(request);
        using var unknownResponse = await _client.SendAsync(unknownRequest);

        Uri? location = response.Headers.Location;
        string locationPath = location is null ? "none"
            : location.IsAbsoluteUri ? location.AbsolutePath
            : location.OriginalString.Split('?', '#')[0];
        await Assert.That(response.StatusCode).IsEqualTo(unknownResponse.StatusCode)
            .Because($"The retired route must follow native unknown-route handling; redirect path: {locationPath}.");
        await Assert.That(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            .IsTrue()
            .Because($"Both absent routes must be rejected. Actual HTTP {(int)response.StatusCode}; redirect path: {locationPath}.");
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo(unknownResponse.Content.Headers.ContentType?.MediaType);
        await Assert.That(location).IsNull();
        await Assert.That(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(cookie => cookie.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal)))
            .IsFalse();
        await Assert.That(await response.Content.ReadAsStringAsync()).DoesNotContain(_accessToken);
    }

    [Test]
    public async Task LoginWithoutAntiforgeryTokenIsRejected()
    {
        using var response = await _client.PostAsJsonAsync(
            "/bff/auth/local/login",
            CreateLoginRequest());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task LoginCreatesCookieWithoutReturningAccessTokenToBrowser()
    {
        string antiforgeryToken = await IssueAntiforgeryCookieAsync();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/bff/auth/local/login")
        {
            Content = JsonContent.Create(CreateLoginRequest())
        };
        request.Headers.Add("X-CSRF-TOKEN", antiforgeryToken);

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain(_accessToken);
        await Assert.That(response.Headers.TryGetValues(
            "Set-Cookie",
            out var setCookies)).IsTrue();
        await Assert.That(setCookies!.Any(cookie =>
            cookie.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal)
            && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    [Arguments("email_verification_required", 401, true)]
    [Arguments("untrusted_provider_detail", 401, false)]
    [Arguments("email_verification_required", 403, false)]
    public async Task LoginFailureExposesOnlyAllowlistedVerificationCode(string failureCode, int statusCode, bool exposesCode)
    {
        await using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILocalAuthClient>();
            services.AddSingleton<ILocalAuthClient>(new LocalAuthClientStub(
                accessToken: _accessToken, failureCode: failureCode, statusCode: statusCode));
        }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        using var tokenResponse = await client.GetAsync("/auth/status");
        string token = tokenResponse.Headers.GetValues("Set-Cookie")
            .Select(ReadXsrfToken).First(value => !string.IsNullOrWhiteSpace(value))!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/auth/local/login")
        {
            Content = JsonContent.Create(CreateLoginRequest())
        };
        request.Headers.Add("X-CSRF-TOKEN", token);

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo((HttpStatusCode)statusCode);
        string body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        await Assert.That(document.RootElement.TryGetProperty("code", out var code)).IsEqualTo(exposesCode);
        if (exposesCode)
        {
            await Assert.That(code.GetString()).IsEqualTo("email_verification_required");
        }
        else
        {
            await Assert.That(body).DoesNotContain(failureCode);
        }
        await Assert.That(body).DoesNotContain(_accessToken);
        await Assert.That(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(cookie => cookie.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal)))
            .IsFalse();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _rootFactory.DisposeAsync();
    }

    private async Task<string> IssueAntiforgeryCookieAsync()
    {
        using var response = await _client.GetAsync("/auth/status");
        await Assert.That(response.Headers.TryGetValues(
            "Set-Cookie",
            out var setCookies)).IsTrue();
        string? token = setCookies!
            .Select(ReadXsrfToken)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        await Assert.That(token).IsNotNull();
        return token!;
    }

    private static string? ReadXsrfToken(string setCookie)
    {
        const string prefix = "XSRF-TOKEN=";
        if (!setCookie.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        int end = setCookie.IndexOf(';', prefix.Length);
        string rawValue = end < 0
            ? setCookie[prefix.Length..]
            : setCookie[prefix.Length..end];
        return Uri.UnescapeDataString(rawValue);
    }

    private static LocalBffLoginRequest CreateLoginRequest() =>
        new()
        {
            Email = "admin@example.test",
            Password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}",
            ReturnUrl = "/dashboard"
        };

    private static string CreateAccessToken()
    {
        byte[] key = RandomNumberGenerator.GetBytes(64);
        DateTime now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: "islamu-event-local",
            audience: "islamu-event-api",
            claims:
            [
                new Claim(
                    JwtRegisteredClaimNames.Sub,
                    Guid.CreateVersion7().ToString("D"))
            ],
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(30),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class LocalAuthClientStub(string accessToken, string? failureCode = null, int statusCode = 401)
        : ILocalAuthClient
    {
        public Task<LocalAuthResponseDto> LoginLocalIdentityAsync(
            LocalAuthRequestDto body,
            string? api_version = null,
            string? x_Api_Version = null,
            CancellationToken cancellationToken = default)
        {
            if (failureCode is not null)
            {
                throw new ApiException<ProblemDetails>(
                    message: "Local login rejected", statusCode: statusCode, response: null,
                    headers: new Dictionary<string, IEnumerable<string>>(),
                    result: new ProblemDetails
                    {
                        Title = accessToken,
                        Detail = accessToken,
                        AdditionalProperties = new Dictionary<string, object> { ["code"] = failureCode }
                    },
                    innerException: null);
            }
            return Task.FromResult(new LocalAuthResponseDto
            {
                Success = true,
                FailureCode = string.Empty,
                UserId = Guid.CreateVersion7(),
                Email = body.Email,
                FirstName = "Site",
                LastName = "Administrator",
                EmailVerified = false,
                Roles = [],
                Token = accessToken,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
            });
        }
    }

    private sealed class LocalPrimarySchemeManager
        : IDynamicAuthSchemeManager
    {
        public Task InitializeAsync() => Task.CompletedTask;

        public Task RefreshSchemesAsync(string? setupSecret = null) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<string>>
            GetRegisteredProviderSchemesAsync() =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public string GetActivePrimaryProvider() => "local";
    }

    private sealed class CompletedOnboardingStatusProvider
        : IBffOnboardingStatusProvider
    {
        public Task<BffOnboardingStatus> GetStatusAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BffOnboardingStatus(
                true,
                "completed",
                "interactive",
                null,
                null,
                BffOnboardingDisposition.Completed));

        public void Invalidate()
        {
        }
    }
}
