using System.Text.Json;
using System.Text.Json.Serialization;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Serialization;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Features.Moderation.Requests.Queries;
using Explore.Domain;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;

namespace Event.Api.IntegrationTests.Features;

[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
public sealed class ModerationIntentOpenApiTests(ContractApiFixture fixture)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new ModerationAuthorityScopeJsonConverter(), new ModerationSubjectReferenceJsonConverter(),
            new ModerationRuleReferenceJsonConverter(), new ModerationProposalJsonConverter(),
            new ModerationClassificationMaterialJsonConverter(), new JsonStringEnumConverter()
        }
    };

    [Test]
    [Arguments(false, 0)]
    [Arguments(false, 1)]
    [Arguments(false, 2)]
    [Arguments(false, 3)]
    [Arguments(false, 4)]
    [Arguments(true, 0)]
    [Arguments(true, 1)]
    [Arguments(true, 2)]
    [Arguments(true, 3)]
    [Arguments(true, 4)]
    public async Task BothDocumentsDescribeOnlyTheActualFactoryPreservingWireFields(bool swagger, int kind)
    {
        var origin = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var rule = ModerationRuleReference.Create(Guid.CreateVersion7(), "conduct", 1);
        var start = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        object payload = kind switch
        {
            0 => origin,
            1 => SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), origin),
            2 => rule,
            4 => new ModerationClassificationMaterial(Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            _ => new ModerationProposal(Guid.CreateVersion7(), rule, [ModerationCapability.Publish],
                start, start.AddHours(2), start.AddHours(1), 1)
        };
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync(swagger ? "/swagger/v0.1/swagger.json" : "/openapi/islamu-event.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var type = payload.GetType();
        var schema = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty(swagger ? type.FullName! : type.Name);
        var wire = JsonSerializer.SerializeToElement(payload, type, Options);
        var properties = schema.GetProperty("properties");
        await Assert.That(properties.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(wire.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal))).IsTrue();
        await Assert.That(schema.GetProperty("additionalProperties").GetBoolean()).IsFalse();
        await Assert.That(schema.GetProperty("required").EnumerateArray()
            .All(property => wire.TryGetProperty(property.GetString()!, out _))).IsTrue();
        if (kind == 3)
        {
            var allowed = properties.GetProperty("capabilities").GetProperty("items").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()).ToArray();
            await Assert.That(allowed.Contains(nameof(ModerationCapability.Publish))).IsTrue();
            await Assert.That(allowed.Intersect(["ProtectedStatus", "Contestation", "Support", "PrivacyErasure"]).Any()).IsFalse();
            await Assert.That(properties.GetProperty("requiredApprovals").GetProperty("enum").EnumerateArray()
                .Select(value => value.GetInt32()).SequenceEqual([1, 2])).IsTrue();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AncestryAndSubjectOriginReferenceTheActualRecursiveScopeContract(bool swagger)
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync(swagger ? "/swagger/v0.1/swagger.json" : "/openapi/islamu-event.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var scopeId = swagger ? typeof(AuthorityScope).FullName! : nameof(AuthorityScope);
        var subjectId = swagger ? typeof(SubjectReference).FullName! : nameof(SubjectReference);
        var parent = schemas.GetProperty(scopeId).GetProperty("properties").GetProperty("parent")
            .GetProperty("oneOf").EnumerateArray().Single(part => part.TryGetProperty("$ref", out _));
        await Assert.That(parent.GetProperty("$ref").GetString()).IsEqualTo($"#/components/schemas/{scopeId}");
        await Assert.That(schemas.GetProperty(subjectId).GetProperty("properties").GetProperty("origin")
            .GetProperty("$ref").GetString()).IsEqualTo($"#/components/schemas/{scopeId}");
        await Assert.That(schemas.GetProperty(subjectId).GetProperty("required").EnumerateArray()
            .Select(value => value.GetString()).Contains("origin")).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualProposalRequestUsesTheQualifiedSubjectTargetAndProposalComponents(bool swagger)
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync(swagger ? "/swagger/v0.1/swagger.json" : "/openapi/islamu-event.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var commandId = swagger ? typeof(ProposeModerationDecisionCommand).FullName! : nameof(ProposeModerationDecisionCommand);
        var properties = schemas.GetProperty(commandId).GetProperty("properties");
        foreach (var (property, type) in new[]
        {
            ("subject", typeof(SubjectReference)), ("target", typeof(AuthorityScope)), ("proposal", typeof(ModerationProposal))
        })
        {
            var id = swagger ? type.FullName! : type.Name;
            await Assert.That(properties.GetProperty(property).GetProperty("$ref").GetString())
                .IsEqualTo($"#/components/schemas/{id}");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProfileProcedureRequestReferencesTheActualTypedFieldVocabulary(bool swagger)
    {
        var procedure = new ModerationProfileCollectionProcedure(
            ModerationIndividualPurpose.ProfileCompletion,
            ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment,
            [ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration,
                ModerationProfilePreliminaryFieldKind.SubjectConsentReference],
            [ModerationProfileNormalFieldKind.DateOfBirth],
            "Independent relationship review.", "Independent manual issuer review.");
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync(swagger ? "/swagger/v0.1/swagger.json" : "/openapi/islamu-event.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var procedureId = swagger ? typeof(ModerationProfileCollectionProcedure).FullName!
            : nameof(ModerationProfileCollectionProcedure);
        var commandId = swagger ? typeof(RecordModerationProfileCollectionProcedureCommand).FullName!
            : nameof(RecordModerationProfileCollectionProcedureCommand);
        var command = schemas.GetProperty(commandId);
        await Assert.That(command.GetProperty("properties").GetProperty("procedure").GetProperty("$ref").GetString())
            .IsEqualTo($"#/components/schemas/{procedureId}");
        var properties = schemas.GetProperty(procedureId).GetProperty("properties");
        var wire = JsonSerializer.SerializeToElement(procedure, Options);
        await Assert.That(properties.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(wire.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal))).IsTrue();
        foreach (var field in new[] { "preliminaryFields", "normalFields" })
            await Assert.That(properties.GetProperty(field).GetProperty("type").GetString()).IsEqualTo("array");
        var required = command.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray();
        foreach (var field in new[]
        {
            "sourceId", "childAgeSourceId", "expectedChildAgeSourceRevision", "expectedChildAgeSourceWriterGeneration",
            "childAgeAdoptionId", "expectedChildAgeAdoptionRevision", "expectedChildAgeAdoptionWriterGeneration",
            "procedure", "validUntil"
        })
            await Assert.That(required.Contains(field)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeHalMaterialReadsKeepTypedInventoryAndSelectionCoordinates(bool review)
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync("/openapi/islamu-event.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var view = review ? nameof(ModerationClassificationMaterialReviewView)
            : nameof(ModerationClassificationMaterialInventoryView);
        var item = review ? nameof(ModerationClassificationMaterialSelection)
            : nameof(ModerationClassificationMaterial);
        foreach (var name in new[] { view, $"HalResourceOf{view}" })
        {
            var properties = schemas.GetProperty(name).GetProperty("properties");
            await Assert.That(properties.GetProperty("materials").GetProperty("items")
                .GetProperty("$ref").GetString()).IsEqualTo($"#/components/schemas/{item}");
            await Assert.That(properties.GetProperty("components").GetProperty("items")
                .GetProperty("$ref").GetString())
                .IsEqualTo($"#/components/schemas/{nameof(ModerationClassificationMaterialComponentVersion)}");
        }
        await Assert.That(schemas.GetProperty(nameof(ModerationClassificationMaterialSelection))
            .GetProperty("properties").GetProperty("material").GetProperty("$ref").GetString())
            .IsEqualTo($"#/components/schemas/{nameof(ModerationClassificationMaterial)}");
    }
}
