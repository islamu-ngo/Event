using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;

namespace Event.Api.IntegrationTests.Features;

public sealed class WebPushPublicConfigurationContractTests
{
    private const string ConfigurationUrl = "/api/notification/web-push/config";
    private const string PublicKeyUrl = "/vapid-public-key";

    [Test]
    public async Task UnconfiguredInstance_PublicReads_ReportDisabledCapabilityWithoutKeyMaterial()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();

        using var configurationResponse = await client.GetAsync(ConfigurationUrl);
        using var publicKeyResponse = await client.GetAsync(PublicKeyUrl);

        await Assert.That(configurationResponse.StatusCode).IsEqualTo(HttpStatusCode.OK)
            .Because("absent optional configuration is a capability state, not a server error");
        await Assert.That(publicKeyResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(
            await configurationResponse.Content.ReadAsStringAsync());
        await Assert.That(document.RootElement.GetProperty("enabled").GetBoolean()).IsFalse()
            .Because("clients must be able to branch on an explicit disabled flag");
        await Assert.That(document.RootElement.GetProperty("publicKey").GetString())
            .IsEqualTo(string.Empty);
        await Assert.That(await publicKeyResponse.Content.ReadAsStringAsync())
            .IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ConfiguredInstance_PublicReads_ExposeOnlyBrowserSafePublicMaterial()
    {
        string publicKey = CreateVapidKey(65);
        string privateKey = CreateVapidKey(32);
        await using var factory = new AuthenticatedWebApplicationFactory();
        factory.AdditionalConfiguration["WebPush:Enabled"] = "true";
        factory.AdditionalConfiguration["WebPush:VapidSubject"] = "mailto:operator@example.test";
        factory.AdditionalConfiguration["WebPush:VapidPublicKey"] = publicKey;
        factory.AdditionalConfiguration["WebPush:VapidPrivateKey"] = privateKey;
        using var client = factory.CreateClient();

        using var configurationResponse = await client.GetAsync(ConfigurationUrl);
        using var publicKeyResponse = await client.GetAsync(PublicKeyUrl);
        string configurationBody = await configurationResponse.Content.ReadAsStringAsync();

        await Assert.That(configurationResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(publicKeyResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(configurationBody);
        await Assert.That(document.RootElement.GetProperty("enabled").GetBoolean()).IsTrue();
        await Assert.That(document.RootElement.GetProperty("publicKey").GetString())
            .IsEqualTo(publicKey);
        await Assert.That(await publicKeyResponse.Content.ReadAsStringAsync()).IsEqualTo(publicKey);
        await Assert.That(configurationBody).DoesNotContain(privateKey)
            .Because("private signing material must never reach a browser-facing read");
        await Assert.That(configurationBody).DoesNotContain("operator@example.test")
            .Because("the VAPID subject is operator contact metadata, not browser-safe material");
    }

    /// <summary>
    /// Produces URL-safe Base64 material of the exact decoded length the settings validator requires,
    /// generated per run so no key material is ever written into the repository.
    /// </summary>
    private static string CreateVapidKey(int byteLength)
    {
        byte[] material = RandomNumberGenerator.GetBytes(byteLength);
        if (byteLength == 65)
        {
            material[0] = 0x04;
        }

        return Convert.ToBase64String(material)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
