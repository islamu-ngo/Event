using System.Text.Json;
using Explore.Application.DTOs.Localization;
using Explore.Domain.Common.Localization;

namespace Event.Application.UnitTests.Localization;

public sealed class LocalizationBundleInputBoundaryTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Test]
    [Arguments("""{"languageCode":"en","languageCode":"fr","translations":{"ui.test":"data"}}""")]
    [Arguments("""{"languageCode":"en","LanguageCode":"fr","translations":{"ui.test":"data"}}""")]
    [Arguments("""{"languageCode":"en","translations":{"ui.test":"first","ui.test":"second"}}""")]
    [Arguments("""{"languageCode":"en","translations":{"ui.test":{"$type":"System.Object"}}}""")]
    [Arguments("""{"languageCode":"en","translations":{"ui.test":null}}""")]
    [Arguments("""{"languageCode":"en","translations":{"ui.test":["data"]}}""")]
    [Arguments("""{"languageCode":"en","translations":{"ui.test":"data"},"$type":"System.Object"}""")]
    public async Task Decode_RejectsAmbiguousOrNonDataInput(string json)
    {
        await Assert.That(() => JsonSerializer.Deserialize<ImportLocalizationBundleDto>(json, WebOptions))
            .Throws<JsonException>();
    }

    [Test]
    public async Task Decode_PreservesLiteralTemplatesAndControlTextWithoutInterpretation()
    {
        const string value = "{{operator.secret}} <script>fetch('https://example.test')</script> $(whoami) & \u0627\u0644\u0639\u0631\u0628\u064a\u0629";
        string json = JsonSerializer.Serialize(new
        {
            languageCode = "en",
            translations = new Dictionary<string, string> { ["ui.test"] = value }
        });

        ImportLocalizationBundleDto decoded =
            JsonSerializer.Deserialize<ImportLocalizationBundleDto>(json, WebOptions)!;
        ImportLocalizationBundleDto roundTrip = JsonSerializer.Deserialize<ImportLocalizationBundleDto>(
            JsonSerializer.Serialize(decoded, WebOptions), WebOptions)!;

        await Assert.That(decoded.LanguageCode).IsEqualTo("en");
        await Assert.That(decoded.Translations["ui.test"]).IsEqualTo(value);
        await Assert.That(roundTrip.Translations["ui.test"]).IsEqualTo(value);
    }

    [Test]
    public async Task Decode_RejectsValueAndAggregateExpansionBeforePublishingDictionary()
    {
        string oversizedValue = JsonSerializer.Serialize(new
        {
            languageCode = "en",
            translations = new Dictionary<string, string>
            {
                ["ui.test"] = new string('x', LocalizationBundlePolicy.MaximumValueCharacters + 1)
            }
        });
        var translations = Enumerable.Range(0, 33).ToDictionary(
            index => $"ui.test{index}",
            _ => new string('x', LocalizationBundlePolicy.MaximumValueCharacters));
        string excessiveAggregate = JsonSerializer.Serialize(new { languageCode = "en", translations });

        await Assert.That(() => JsonSerializer.Deserialize<ImportLocalizationBundleDto>(oversizedValue, WebOptions))
            .Throws<JsonException>();
        await Assert.That(() => JsonSerializer.Deserialize<ImportLocalizationBundleDto>(excessiveAggregate, WebOptions))
            .Throws<JsonException>();
    }

    [Test]
    public async Task Decode_RejectsExcessiveCountAndRawInputBytes()
    {
        var translations = Enumerable.Range(0, LocalizationBundlePolicy.MaximumEntries + 1)
            .ToDictionary(index => $"ui.test{index}", _ => "");
        string excessiveCount = JsonSerializer.Serialize(new { languageCode = "en", translations });
        string excessiveBytes = "{\"languageCode\":\"en\",\"translations\":{"
            + new string(' ', LocalizationBundlePolicy.MaximumBytes) + "\"ui.test\":\"data\"}}";

        await Assert.That(() => JsonSerializer.Deserialize<ImportLocalizationBundleDto>(excessiveCount, WebOptions))
            .Throws<JsonException>();
        await Assert.That(() => JsonSerializer.Deserialize<ImportLocalizationBundleDto>(excessiveBytes, WebOptions))
            .Throws<JsonException>();
    }
}
