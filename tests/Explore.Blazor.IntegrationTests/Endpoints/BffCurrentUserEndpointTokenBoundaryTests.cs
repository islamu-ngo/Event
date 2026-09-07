
namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class BffCurrentUserEndpointTokenBoundaryTests
{
    [Test]
    public async Task CurrentUser_WhenPrincipalContainsTokenClaims_DoesNotReturnTokens()
    {
        const string accessToken = "server-held-access-token";
        const string refreshToken = "server-held-refresh-token";
        const string idToken = "server-held-id-token";

        using var factory = new BlazorBffWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var headerValue = TestAuthHandler.CreateAuthHeaderValue(
            Guid.NewGuid(),
            "Token Boundary User",
            ("access_token", accessToken),
            ("refresh_token", refreshToken),
            ("id_token", idToken),
            ("name", "Token Boundary User"),
            ("email", "token-boundary@example.test"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/bff/me");
        request.Headers.Add(TestAuthHandler.AuthHeaderName, headerValue);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).Contains("Token Boundary User");
        await Assert.That(body).DoesNotContain("access_token");
        await Assert.That(body).DoesNotContain("refresh_token");
        await Assert.That(body).DoesNotContain("id_token");
        await Assert.That(body).DoesNotContain(accessToken);
        await Assert.That(body).DoesNotContain(refreshToken);
        await Assert.That(body).DoesNotContain(idToken);
    }
}
