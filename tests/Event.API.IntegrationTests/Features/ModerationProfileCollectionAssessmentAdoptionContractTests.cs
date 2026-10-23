using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Serialization;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationProfileCollectionAssessmentAdoptionContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ModerationAuthorityScopeJsonConverter(), new JsonStringEnumConverter() }
    };

    [Test]
    [Arguments(nameof(ModerationProfileCollectionAssessmentsController.Record), "sources",
        RouteNames.RecordModerationProfileCollectionAssessment)]
    [Arguments(nameof(ModerationProfileCollectionAssessmentsController.Adopt), "adoptions",
        RouteNames.AdoptModerationProfileCollectionAssessment)]
    public async Task NativeWritesHaveDistinctNamedRoutesAndCannotStoreReplayResponses(string action, string route, string name)
    {
        var method = typeof(ModerationProfileCollectionAssessmentsController).GetMethod(action)!;
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Template).IsEqualTo(route);
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Name).IsEqualTo(name);
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
    }

    [Test]
    [Arguments("subjectHumanId")]
    [Arguments("principal")]
    [Arguments("observedAt")]
    [Arguments("isQualified")]
    [Arguments("dateOfBirth")]
    [Arguments("providerKey")]
    [Arguments("$type")]
    public async Task CallerAuthorityAndSensitiveFieldsCannotBindAtEitherObjectLevel(string member)
    {
        foreach (var command in new object[] { Record(), Adopt() })
        {
            foreach (var nested in new[] { false, true })
            {
                var payload = JsonSerializer.SerializeToNode(command, command.GetType(), Options)!.AsObject();
                (nested ? payload["original"]!.AsObject() : payload)[member] = "unsupported assertion";
                await Assert.That(() => JsonSerializer.Deserialize(payload.ToJsonString(), command.GetType(), Options))
                    .Throws<JsonException>();
            }
        }
    }

    [Test]
    [Arguments(false, "original")]
    [Arguments(false, "sourceId")]
    [Arguments(false, "expectedGeneration")]
    [Arguments(false, "required")]
    [Arguments(false, "validUntil")]
    [Arguments(true, "original")]
    [Arguments(true, "sourceId")]
    [Arguments(true, "expectedSourceRevision")]
    [Arguments(true, "expectedSourceGeneration")]
    [Arguments(true, "expectedGeneration")]
    [Arguments(true, "adoptionId")]
    [Arguments(true, "validUntil")]
    public async Task CompleteTopLevelIntentCannotBeOmitted(bool adopt, string member)
    {
        object command = adopt ? Adopt() : Record();
        var payload = JsonSerializer.SerializeToNode(command, command.GetType(), Options)!.AsObject();
        payload.Remove(member);
        await Assert.That(() => JsonSerializer.Deserialize(payload.ToJsonString(), command.GetType(), Options))
            .Throws<JsonException>();
    }

    [Test]
    [Arguments("observationId")]
    [Arguments("expectedObservationRevision")]
    [Arguments("expectedObservationGeneration")]
    [Arguments("jurisdiction")]
    [Arguments("policyId")]
    [Arguments("policyRevision")]
    [Arguments("publicationId")]
    [Arguments("profileId")]
    [Arguments("mode")]
    [Arguments("procedureSourceId")]
    [Arguments("expectedProcedureSourceRevision")]
    [Arguments("expectedProcedureSourceWriterGeneration")]
    [Arguments("procedureAdoptionId")]
    [Arguments("expectedProcedureAdoptionRevision")]
    [Arguments("expectedProcedureAdoptionWriterGeneration")]
    public async Task OriginalObservationAndParentCoordinatesCannotBeOmitted(string member)
    {
        foreach (var command in new object[] { Record(), Adopt() })
        {
            var payload = JsonSerializer.SerializeToNode(command, command.GetType(), Options)!.AsObject();
            payload["original"]!.AsObject().Remove(member);
            await Assert.That(() => JsonSerializer.Deserialize(payload.ToJsonString(), command.GetType(), Options))
                .Throws<JsonException>();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TypedJsonPreservesExplicitEmptyOrCompleteRequirementsAndOriginalCoordinates(bool complete)
    {
        var command = Record() with { Required = complete
            ? [ModerationCollectionPrerequisiteKind.AccountEligibility, ModerationCollectionPrerequisiteKind.SubjectConsent,
                ModerationCollectionPrerequisiteKind.GuardianConsent] : [] };
        var copy = JsonSerializer.Deserialize<RecordModerationProfileCollectionAssessmentCommand>(
            JsonSerializer.Serialize(command, Options), Options)!;
        await Assert.That(copy.Original).IsEqualTo(command.Original);
        await Assert.That(copy.Required.SequenceEqual(command.Required)).IsTrue();
        await Assert.That(copy.ToRecord().Original).IsEqualTo(command.ToRecord().Original);
        var acceptance = Adopt();
        var adopted = JsonSerializer.Deserialize<AdoptModerationProfileCollectionAssessmentCommand>(
            JsonSerializer.Serialize(acceptance, Options), Options)!;
        await Assert.That(adopted.ToAdoption()).IsEqualTo(acceptance.ToAdoption());
    }

    private static RecordModerationProfileCollectionAssessmentCommand Record() => new()
    {
        Original = Original(), SourceId = Id(7), ExpectedGeneration = 0, Required = [],
        ValidUntil = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero)
    };

    private static AdoptModerationProfileCollectionAssessmentCommand Adopt() => new()
    {
        Original = Original(), SourceId = Id(7), ExpectedSourceRevision = 1, ExpectedSourceGeneration = 1,
        ExpectedGeneration = 1, AdoptionId = Id(8),
        ValidUntil = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero)
    };

    private static ModerationProfileCollectionAssessmentOriginalIntent Original() => new()
    {
        ObservationId = Id(1), ExpectedObservationRevision = 1, ExpectedObservationGeneration = 2,
        Jurisdiction = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Id(30)), Id(31)), Id(32)), Id(33)),
        PolicyId = Id(2), PolicyRevision = 4, PublicationId = Id(3), ProfileId = Id(4),
        Mode = ModerationNativeMode.Embedded, ProcedureSourceId = Id(5), ExpectedProcedureSourceRevision = 1,
        ExpectedProcedureSourceWriterGeneration = 7, ProcedureAdoptionId = Id(6),
        ExpectedProcedureAdoptionRevision = 1, ExpectedProcedureAdoptionWriterGeneration = 8
    };

    private static Guid Id(int value) => Guid.Parse($"019a0000-0000-7000-8000-{value:x12}");
}
