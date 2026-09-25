using System.Text.Json;
using Explore.API.Hateoas;
using Explore.Domain.ValueObjects;

namespace Event.Architecture.Tests;

public sealed class EventResourceFileOpenApiTests
{
    [Test]
    public async Task ContentIsBinaryForEachSupportedDocumentTypeRatherThanAnMvcObject()
    {
        await using var input = GeneratedContractInputs.OpenSchema();
        using var document = await JsonDocument.ParseAsync(input);
        var operation = document.RootElement.GetProperty("paths")
            .GetProperty("/api/eventresource/{id}/content").GetProperty("get");
        await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo(RouteNames.GetEventResourceContent);
        var content = operation.GetProperty("responses").GetProperty("200").GetProperty("content");
        string[] types =
        [
            EventResourceGovernancePolicy.PdfMediaType,
            EventResourceGovernancePolicy.WordDocumentMediaType,
            EventResourceGovernancePolicy.PowerPointPresentationMediaType
        ];
        await Assert.That(content.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
            .SetEquals(types)).IsTrue();
        foreach (string type in types)
        {
            var schema = content.GetProperty(type).GetProperty("schema");
            await Assert.That(schema.GetProperty("type").GetString()).IsEqualTo("string");
            await Assert.That(schema.GetProperty("format").GetString()).IsEqualTo("binary");
        }
    }

    [Test]
    public async Task FileDescriptorsExcludeStorageAndInspectionIdentity()
    {
        await using var input = GeneratedContractInputs.OpenSchema();
        using var document = await JsonDocument.ParseAsync(input);
        var fields = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("EventResourceFileMetadataDto").GetProperty("properties")
            .EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        await Assert.That(fields.SetEquals(["fileName", "contentType", "sizeBytes", "safetyState"])).IsTrue();
    }
}
