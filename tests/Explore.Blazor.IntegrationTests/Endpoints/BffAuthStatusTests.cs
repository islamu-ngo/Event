namespace Explore.Blazor.IntegrationTests.Endpoints;

public class BffAuthStatusTests
{
    [Test]
    public async Task AuthStatus_Anonymous_ReturnsNotAuthenticated()
    {
        using var factory = new BlazorBffWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/auth/status");
        var payload = await response.Content.ReadFromJsonAsync<AuthStatusResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(payload).IsNotNull();
        await Assert.That(payload!.IsAuthenticated).IsFalse();
    }

    [Test]
    public async Task AuthStatus_Authenticated_ReturnsUserInfo()
    {
        using var factory = new BlazorBffWebApplicationFactory();
        using var client = factory.CreateClient();

        var userId = Guid.NewGuid();
        var headerValue = TestAuthHandler.CreateAuthHeaderValue(userId, "Test User");
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, headerValue);

        var response = await client.GetAsync("/auth/status");
        var payload = await response.Content.ReadFromJsonAsync<AuthStatusResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(payload).IsNotNull();
        await Assert.That(payload!.IsAuthenticated).IsTrue();
        await Assert.That(payload.Name).IsEqualTo("Test User");
    }

    [Test]
    public async Task AuthStatus_ReturnsOkStatusCode()
    {
        using var factory = new BlazorBffWebApplicationFactory();

        using var anonymousClient = factory.CreateClient();
        var anonymousResponse = await anonymousClient.GetAsync("/auth/status");

        using var authenticatedClient = factory.CreateClient();
        var userId = Guid.NewGuid();
        var headerValue = TestAuthHandler.CreateAuthHeaderValue(userId, "Status Check User");
        authenticatedClient.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, headerValue);
        var authenticatedResponse = await authenticatedClient.GetAsync("/auth/status");

        await Assert.That(anonymousResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(authenticatedResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private sealed class AuthStatusResponse
    {
        public bool IsAuthenticated { get; set; }

        public string? Name { get; set; }
    }
}
