using System.Text.Json;

namespace Event.Architecture.Tests;

/// <summary>
/// Verifies the generated success contract cannot advertise an absent or undefined credential disclosure outcome.
/// </summary>
public sealed class ExternalApiKeyOpenApiContractTests
{
    /// <summary>
    /// Requires the two acknowledged success outcomes without widening the public enum to null.
    /// </summary>
    [Test]
    public async Task IssuanceDisclosureEnumContainsOnlyAcknowledgedSuccessOutcomes()
    {
        using JsonDocument document = await ReadOpenApiAsync();
        JsonElement disclosure = document.RootElement.GetProperty("components")
            .GetProperty("schemas").GetProperty("ExternalApiKeyDisclosureStatus").GetProperty("enum");
        string?[] values = disclosure.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : null)
            .ToArray();

        await Assert.That(values.SequenceEqual(["Issued", "PreviouslyIssued"])).IsTrue();
    }

    /// <summary>
    /// Checks required presence independently of the enum values in the endpoint's actual success schema.
    /// </summary>
    [Test]
    public async Task IssuanceSuccessRequiresDisclosureStatus()
    {
        using JsonDocument document = await ReadOpenApiAsync();
        JsonElement root = document.RootElement;
        JsonElement responseSchema = root.GetProperty("paths").GetProperty("/api/externalapikey")
            .GetProperty("post").GetProperty("responses").GetProperty("200")
            .GetProperty("content").EnumerateObject()
            .First(content => content.Name.StartsWith("application/json", StringComparison.Ordinal))
            .Value.GetProperty("schema");
        string reference = responseSchema.GetProperty("$ref").GetString()!;
        JsonElement success = root.GetProperty("components").GetProperty("schemas")
            .GetProperty(reference["#/components/schemas/".Length..]);

        await Assert.That(success.GetProperty("required").EnumerateArray()
            .Any(property => property.GetString() == "disclosureStatus")).IsTrue();
    }

    /// <summary>
    /// Uses the build-captured schema that produced the compiled client rather than a mutable workspace file.
    /// </summary>
    private static async Task<JsonDocument> ReadOpenApiAsync()
    {
        await using Stream stream = GeneratedContractInputs.OpenSchema();
        return await JsonDocument.ParseAsync(stream);
    }
}
