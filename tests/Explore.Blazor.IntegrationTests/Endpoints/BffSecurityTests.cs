using System.Text.RegularExpressions;
using Explore.Blazor.IntegrationTests.Fixtures;
using TUnit.Core;

namespace Explore.Blazor.IntegrationTests.Endpoints;

/// <summary>
/// Security integration tests for the Blazor BFF against a containerized Keycloak.
/// Exercises the real OIDC challenge flow, provider readiness checks,
/// and auth status endpoint behavior.
///
/// These tests prove:
/// 1. The BFF correctly redirects to the containerized Keycloak for OIDC challenge.
/// 2. The redirect includes PKCE (S256), nonce, state, and correct client_id.
/// 3. Auth status endpoint correctly reports anonymous vs. authenticated state.
/// 4. Signout endpoint correctly clears the cookie session.
/// </summary>
[Category(BffTestCategories.Security)]
[Category(BffTestCategories.Runtime)]
[Explicit]
[ClassDataSource<BffKeycloakFixture>(Shared = SharedType.PerAssembly)]
public class BffSecurityTests : IAsyncDisposable
{
    private readonly BffKeycloakFixture _keycloak;
    private readonly SecurityBlazorBffWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BffSecurityTests(BffKeycloakFixture keycloak)
    {
        _keycloak = keycloak;
        _factory = new SecurityBlazorBffWebApplicationFactory(
            keycloakAuthority: keycloak.Authority,
            keycloakMetadataAddress: keycloak.MetadataAddress,
            keycloakClientId: BffKeycloakFixture.TestClientId,
            keycloakClientSecret: keycloak.ClientSecret);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    #region Auth Status — Anonymous

    [Test]
    public async Task AuthStatus_Anonymous_ReturnsNotAuthenticated()
    {
        var response = await _client.GetAsync("/auth/status");

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<AuthStatusPayload>();
        await Assert.That(payload).IsNotNull();
        await Assert.That(payload!.IsAuthenticated).IsFalse().Because("anonymous requests should report not authenticated");
    }

    #endregion

    #region OIDC Challenge Redirect

    [Test]
    public async Task Challenge_KeycloakProvider_ShouldRedirectToKeycloak()
    {
        var response = await _client.GetAsync("/auth/challenge?provider=keycloak&returnUrl=/dashboard");

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Redirect).Because("OIDC challenge must respond with a 302 redirect");

        var location = response.Headers.Location?.ToString();
        await Assert.That(string.IsNullOrEmpty(location)).IsFalse()
            .Because("the redirect must have a Location header");

        await Assert.That(location).Contains("/realms/ISLAMU/protocol/openid-connect/auth").Because("the redirect must point to Keycloak's authorization endpoint");

        await Assert.That(location).Contains("response_type=code").Because("the redirect must request authorization code flow");

        await Assert.That(location).Contains("client_id=islamu-event-blazor").Because("the redirect must include the correct client_id");

        await Assert.That(location).Contains("redirect_uri=").Because("the redirect must include a callback URI");

        await Assert.That(location).Contains("scope=").Because("the redirect must include OIDC scopes");

        await Assert.That(location).DoesNotContain("offline_access").Because("the BFF only needs normal refresh tokens and Keycloak rejects offline-token requests for users without offline access");

        await Assert.That(location).Contains("state=").Because("the redirect must include a state parameter for CSRF protection");
    }

    [Test]
    public async Task Challenge_KeycloakProvider_ShouldIncludeNonce()
    {
        var response = await _client.GetAsync("/auth/challenge?provider=keycloak");

        var location = response.Headers.Location?.ToString();
        await Assert.That(location).Contains("nonce=").Because("the OIDC request must include a nonce for replay protection");
    }

    [Test]
    public async Task Challenge_KeycloakProvider_ShouldIncludePkce()
    {
        var response = await _client.GetAsync("/auth/challenge?provider=keycloak");

        var location = response.Headers.Location?.ToString();
        await Assert.That(location).Contains("code_challenge=").Because("PKCE must be enabled — the challenge must include a code_challenge parameter");
        await Assert.That(location).Contains("code_challenge_method=S256").Because("PKCE must use S256 code challenge method");
    }

    [Test]
    public async Task AuthorizationCodeLogin_ReturnsThroughCallback_WithAuthenticatedBffSession()
    {
        var challenge = await _client.GetAsync("/auth/challenge?provider=keycloak&returnUrl=/dashboard");
        var authorizationUri = challenge.Headers.Location;
        await Assert.That(authorizationUri).IsNotNull();

        using var keycloakHandler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = new CookieContainer()
        };
        using var keycloakClient = new HttpClient(keycloakHandler);

        var loginPage = await keycloakClient.GetAsync(authorizationUri);
        await Assert.That(loginPage.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var loginAction = Regex.Match(
            loginHtml,
            "<form(?=[^>]*id=\"kc-form-login\")(?=[^>]*action=\"(?<action>[^\"]+)\")[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Groups["action"].Value;
        await Assert.That(string.IsNullOrWhiteSpace(loginAction)).IsFalse();
        var loginUri = new Uri(WebUtility.HtmlDecode(loginAction));

        using var credentials = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("username", "test-user"),
            new KeyValuePair<string, string>("password", _keycloak.TestUserPassword),
            new KeyValuePair<string, string>("credentialId", string.Empty),
            new KeyValuePair<string, string>("login", "Sign In")
        ]);
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, loginUri)
        {
            Content = credentials
        };
        var authSessionCookies = keycloakHandler.CookieContainer
            .GetAllCookies()
            .Cast<Cookie>()
            .Select(cookie => $"{cookie.Name}={cookie.Value}");
        loginRequest.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", authSessionCookies));

        var login = await keycloakClient.SendAsync(loginRequest);
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(login.Headers.Location).IsNotNull();

        var callback = await _client.GetAsync(login.Headers.Location);
        await Assert.That(callback.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(callback.Headers.Location?.ToString()).EndsWith("/dashboard");

        var status = await _client.GetFromJsonAsync<AuthStatusPayload>("/auth/status");
        await Assert.That(status).IsNotNull();
        await Assert.That(status!.IsAuthenticated).IsTrue();
    }

    #endregion

    #region Provider Discovery

    [Test]
    public async Task AuthProviders_ShouldReturnKeycloakAsReady()
    {
        var response = await _client.GetAsync("/auth/providers");

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        await Assert.That(json).Contains("Keycloak").Because("the containerized Keycloak should be listed as a provider");
        await Assert.That(json).Contains("keycloak").Because("the provider name should be 'keycloak'");
    }

    #endregion

    #region Challenge — No Provider Specified

    [Test]
    public async Task Challenge_NoProvider_WithSingleReadyProvider_ShouldRedirectToKeycloak()
    {
        var response = await _client.GetAsync("/auth/challenge?returnUrl=/");

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Redirect).Because("with a single registered provider, challenge should auto-redirect");

        var location = response.Headers.Location?.ToString();
        await Assert.That(location).Contains("/realms/ISLAMU/protocol/openid-connect/auth").Because("should auto-select Keycloak when it's the only ready provider");
    }

    #endregion

    #region Login Redirect

    [Test]
    public async Task Login_ShouldRedirectToChallenge()
    {
        var response = await _client.GetAsync("/auth/login?provider=keycloak&returnUrl=/dashboard");

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Redirect);

        var location = response.Headers.Location?.ToString();
        await Assert.That(location).Contains("/auth/challenge").Because("/auth/login should redirect to /auth/challenge");
        await Assert.That(location).Contains("provider=keycloak").Because("the provider should be forwarded to the challenge endpoint");
    }

    #endregion

    #region Signout

    [Test]
    public async Task Signout_Anonymous_ShouldRedirect()
    {
        var response = await _client.GetAsync("/auth/signout?returnUrl=/");

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Redirect).Because("signout should redirect even for anonymous users");
    }

    [Test]
    public async Task Signout_ShouldClearCookies()
    {
        var response = await _client.GetAsync("/auth/signout?returnUrl=/");

        var setCookieHeaders = response.Headers.GetValues("Set-Cookie").ToList();

        await Assert.That(setCookieHeaders).Contains(c =>
            c.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal) &&
            c.Contains("expires=")).Because("signout should expire the authentication cookie");
    }

    #endregion

    private sealed class AuthStatusPayload
    {
        public bool IsAuthenticated { get; set; }
        public string? Name { get; set; }
    }
}
