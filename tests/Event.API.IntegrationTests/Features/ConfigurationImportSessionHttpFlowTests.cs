namespace Event.API.IntegrationTests.Features;

using System.Net;
using System.Net.Http.Headers;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.ConfigurationImport;
using Explore.Application.Contracts.Infrastructure;
using NSubstitute;

public sealed class ConfigurationImportSessionHttpFlowTests
{
    private const string Route =
        "/api/control-plane/configuration-import/sessions";
    private const string MediaType =
        "application/vnd.islamu.configuration-manifest.v1alpha2+json";

    [Test]
    public async Task MissingAuthentication_IsUnauthorizedAndNoStore()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using HttpRequestMessage request = UploadRequest();

        using HttpResponseMessage response =
            await client.SendAsync(request);

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    public async Task ProviderUnavailable_IsSafeNoStoreProblemDetails()
    {
        IAuthorizationProvider provider =
            Substitute.For<IAuthorizationProvider>();
        provider.AuthorizeAsync(
                Arg.Any<AuthorizationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(AuthorizationDecision.Deny(
                AuthorizationProviderMetadata.Cerbos,
                AuthorizationDecisionReasonCodes.ProviderUnavailable));

        using var factory = CreateFactory(provider);
        using var client = factory.CreateClient();
        using HttpRequestMessage request = UploadRequest();
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateInstanceAdminHeaderValue(Guid.NewGuid()));

        using HttpResponseMessage response =
            await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");
        await Assert.That(body)
            .DoesNotContain(
                ConfigurationImportApiBoundary.AccessTokenHeader,
                StringComparison.OrdinalIgnoreCase);
        await Assert.That(body)
            .DoesNotContain("accessToken", StringComparison.OrdinalIgnoreCase);
    }

    private static AuthenticatedWebApplicationFactory CreateFactory(
        IAuthorizationProvider? authorizationProvider = null)
    {
        Environment.SetEnvironmentVariable(
            "SETUP_SECRET",
            "integration-setup-secret");
        return new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = authorizationProvider
        };
    }

    private static HttpRequestMessage UploadRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = new ByteArrayContent("{}"u8.ToArray())
        };
        request.Content.Headers.ContentType =
            MediaTypeHeaderValue.Parse(MediaType);
        return request;
    }
}
