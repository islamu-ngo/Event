using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.IntegrationTests.Fixtures;
using Explore.Blazor.Models;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
                    new SuccessfulLocalAuthClient(_accessToken));
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

    private sealed class SuccessfulLocalAuthClient(string accessToken)
        : ILocalAuthClient
    {
        public Task<LocalAuthResponseDto> LoginLocalIdentityAsync(
            LocalAuthRequestDto body,
            string? api_version = null,
            string? x_Api_Version = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalAuthResponseDto
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

        public Task<LocalRegistrationResponseDto> RegisterLocalIdentityAsync(
            LocalRegistrationRequestDto body,
            string? api_version = null,
            string? x_Api_Version = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
