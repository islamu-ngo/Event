using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Domain.Common.Localization;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class GuardedWireOpenApiContractTests
{
    [Test]
    [Arguments("AiCredentialScope", "None,Instance,Tenant")]
    [Arguments("AiModelDiscoveryScope", "Tenant,Instance")]
    public async Task NativeAiScopesPublishClosedStringValues(string component, string expected)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/islamu-event.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var schema = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty(component);
        await Assert.That(schema.GetProperty("type").GetString()).IsEqualTo("string");
        string[] values = schema.GetProperty("enum").EnumerateArray()
            .Select(value => value.GetString()!).ToArray();
        await Assert.That(values).IsEquivalentTo(expected.Split(','));
    }

    [Test]
    public async Task NativeLocalizationImportPublishesItsStrictTypedEnvelope()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/islamu-event.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var schema = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("ImportLocalizationBundleDto");
        await Assert.That(schema.GetProperty("type").GetString()).IsEqualTo("object");
        await Assert.That(schema.GetProperty("additionalProperties").GetBoolean()).IsFalse();
        string[] required = schema.GetProperty("required").EnumerateArray()
            .Select(value => value.GetString()!).ToArray();
        await Assert.That(required).IsEquivalentTo(new[] { "languageCode", "translations" });
        var properties = schema.GetProperty("properties");
        var language = properties.GetProperty("languageCode");
        await Assert.That(language.GetProperty("type").GetString()).IsEqualTo("string");
        await Assert.That(language.GetProperty("minLength").GetInt32()).IsEqualTo(1);
        await Assert.That(language.GetProperty("maxLength").GetInt32()).IsEqualTo(35);
        var translations = properties.GetProperty("translations");
        await Assert.That(translations.GetProperty("type").GetString()).IsEqualTo("object");
        await Assert.That(translations.GetProperty("maxProperties").GetInt32())
            .IsEqualTo(LocalizationBundlePolicy.MaximumEntries);
        var value = translations.GetProperty("additionalProperties");
        await Assert.That(value.GetProperty("type").GetString()).IsEqualTo("string");
        await Assert.That(value.GetProperty("maxLength").GetInt32())
            .IsEqualTo(LocalizationBundlePolicy.MaximumValueCharacters);
    }
}
