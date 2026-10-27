using System.Text.Json;

namespace Event.Architecture.Tests;

public sealed class PrivateProfileOpenApiContractTests
{
    [Test]
    public async Task DeclaredThresholdHasNamedStringStatesAndNoBirthDateOrIdentityFields()
    {
        await using var input = GeneratedContractInputs.OpenSchema();
        using var document = await JsonDocument.ParseAsync(input);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var display = schemas.GetProperty("ModerationDeclaredAgeDisplay").GetProperty("properties");
        await Assert.That(display.GetProperty("kind").GetProperty("$ref").GetString())
            .IsEqualTo("#/components/schemas/ModerationAgeEvidenceKind");
        await Assert.That(display.GetProperty("outcome").GetProperty("$ref").GetString())
            .IsEqualTo("#/components/schemas/ModerationDeclaredAgeOutcome");
        await Assert.That(schemas.GetProperty("ModerationAgeEvidenceKind").GetProperty("type").GetString()).IsEqualTo("string");
        await Assert.That(schemas.GetProperty("ModerationAgeEvidenceKind").GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString()).ToArray())
            .IsEquivalentTo(new[] { "Unknown", "Declared", "Assured" });
        await Assert.That(display.EnumerateObject().Select(property => property.Name).ToArray())
            .IsEquivalentTo(new[] { "kind", "outcome", "minimumAge", "birthDateRevision", "evaluatedAt", "validUntil" });
        await Assert.That(schemas.GetProperty("PrivateBirthDateStatus").GetProperty("properties")
            .EnumerateObject().Select(property => property.Name).ToArray())
            .IsEquivalentTo(new[] { "revision", "hasDeclaration" });
    }
}
