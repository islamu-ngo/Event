using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Authentication;

namespace Event.Api.IntegrationTests.Privacy;

public sealed class RecognitionCorrectionReceiptApiTests
{
    [Test]
    public async Task OpenApi_requires_only_the_correction_purpose_for_protected_correction()
    {
        await using var factory = new AuthenticatedWebApplicationFactory { SeedActiveDefaultTenant = true };
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/islamu-event.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = document.RootElement.GetProperty("paths")
            .GetProperty("/api/privacy-erasure/recognition-corrections").GetProperty("get");
        var schemes = operation.GetProperty("security").EnumerateArray()
            .SelectMany(requirement => requirement.EnumerateObject().Select(property => property.Name)).ToArray();
        await Assert.That(schemes).IsEquivalentTo(
            new[] { ModerationRecognitionCorrectionReceiptAuthenticationHandler.SchemeName });
        var scheme = document.RootElement.GetProperty("components").GetProperty("securitySchemes")
            .GetProperty(ModerationRecognitionCorrectionReceiptAuthenticationHandler.SchemeName);
        await Assert.That(scheme.GetProperty("type").GetString()).IsEqualTo("apiKey");
        await Assert.That(scheme.GetProperty("in").GetString()).IsEqualTo("header");
        await Assert.That(scheme.GetProperty("name").GetString()).IsEqualTo("Authorization");
    }

    [Test]
    [Arguments(null)]
    [Arguments("Bearer")]
    [Arguments("ErasureReceipt")]
    [Arguments("ErasureCorrectionReceipt")]
    public async Task Account_or_status_authentication_cannot_open_protected_correction(string? scheme)
    {
        await using var factory = new AuthenticatedWebApplicationFactory { SeedActiveDefaultTenant = true };
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/privacy-erasure/recognition-corrections");
        if (scheme is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue(scheme,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        using var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
    }

    [Test]
    public async Task Correction_purpose_header_does_not_authenticate_ordinary_status()
    {
        await using var factory = new AuthenticatedWebApplicationFactory { SeedActiveDefaultTenant = true };
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/privacy-erasure/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("ErasureCorrectionReceipt",
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        using var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}
