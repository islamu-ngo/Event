using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Serialization;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationProfileCollectionProceduresControllerContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ModerationAuthorityScopeJsonConverter(), new JsonStringEnumConverter() }
    };

    [Test]
    [Arguments(nameof(ModerationProfileCollectionProceduresController.Record), "sources",
        RouteNames.RecordModerationProfileCollectionProcedure)]
    [Arguments(nameof(ModerationProfileCollectionProceduresController.Adopt), "adoptions",
        RouteNames.AdoptModerationProfileCollectionProcedure)]
    [Arguments(nameof(ModerationProfileCollectionProceduresController.Withdraw), "adoptions/{adoptionId:guid}/withdraw",
        RouteNames.WithdrawModerationProfileCollectionProcedure)]
    public async Task ProcedureWritesAreAuthenticatedBoundedNamedAndUncached(string action, string template, string name)
    {
        var controller = typeof(ModerationProfileCollectionProceduresController);
        var method = controller.GetMethod(action)!;
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(controller.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(controller.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo("write");
        await Assert.That(((IRequestSizeLimitMetadata?)controller.GetCustomAttribute<RequestSizeLimitAttribute>())?.MaxRequestBodySize)
            .IsEqualTo(64L * 1024);
        await Assert.That(controller.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(controller.GetCustomAttribute<OutputCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Template).IsEqualTo(template);
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Name).IsEqualTo(name);
        await Assert.That(method.GetCustomAttributes<ProducesResponseTypeAttribute>().Any(response =>
            response.StatusCode == StatusCodes.Status200OK && response.Type == typeof(ModerationQualificationResponse))).IsTrue();
    }

    [Test]
    [Arguments("record", "dateOfBirth")]
    [Arguments("record", "isQualified")]
    [Arguments("record", "$type")]
    [Arguments("adopt", "dateOfBirth")]
    [Arguments("adopt", "isQualified")]
    [Arguments("adopt", "$type")]
    [Arguments("withdraw", "dateOfBirth")]
    [Arguments("withdraw", "isQualified")]
    [Arguments("withdraw", "$type")]
    public async Task IndividualInputsAndClaimedAuthorityCannotBind(string kind, string member)
    {
        var command = Command(kind);
        var document = JsonSerializer.SerializeToNode(command, command.GetType(), Options)!.AsObject();
        document[member] = "unrequested caller assertion";
        await Assert.That(() => JsonSerializer.Deserialize(document.ToJsonString(), command.GetType(), Options))
            .Throws<JsonException>();
    }

    [Test]
    [Arguments("sourceId")]
    [Arguments("childAgeSourceId")]
    [Arguments("expectedChildAgeSourceRevision")]
    [Arguments("expectedChildAgeSourceWriterGeneration")]
    [Arguments("childAgeAdoptionId")]
    [Arguments("expectedChildAgeAdoptionRevision")]
    [Arguments("expectedChildAgeAdoptionWriterGeneration")]
    [Arguments("procedure")]
    [Arguments("validUntil")]
    public async Task MissingRequiredParentProcedureCoordinatesCannotBind(string member)
    {
        var document = JsonSerializer.SerializeToNode(Command("record"),
            typeof(RecordModerationProfileCollectionProcedureCommand), Options)!.AsObject();
        document.Remove(member);
        await Assert.That(() => JsonSerializer.Deserialize<RecordModerationProfileCollectionProcedureCommand>(
            document.ToJsonString(), Options)).Throws<JsonException>();
    }

    [Test]
    public async Task TypedProcedureJsonPreservesExplicitFieldChoicesAndOriginalParentCoordinates()
    {
        var command = (RecordModerationProfileCollectionProcedureCommand)Command("record");
        var copy = JsonSerializer.Deserialize<RecordModerationProfileCollectionProcedureCommand>(
            JsonSerializer.Serialize(command, Options), Options)!;
        await Assert.That(copy.OperationId).IsEqualTo(command.OperationId);
        await Assert.That(copy.Target).IsEqualTo(command.Target);
        await Assert.That(copy.ExpectedQualificationRevision).IsEqualTo(command.ExpectedQualificationRevision);
        await Assert.That(copy.ChildAgeSourceId).IsEqualTo(command.ChildAgeSourceId);
        await Assert.That(copy.ChildAgeAdoptionId).IsEqualTo(command.ChildAgeAdoptionId);
        await Assert.That(copy.ExpectedChildAgeSourceRevision).IsEqualTo(command.ExpectedChildAgeSourceRevision);
        await Assert.That(copy.ExpectedChildAgeSourceWriterGeneration).IsEqualTo(command.ExpectedChildAgeSourceWriterGeneration);
        await Assert.That(copy.ExpectedChildAgeAdoptionRevision).IsEqualTo(command.ExpectedChildAgeAdoptionRevision);
        await Assert.That(copy.ExpectedChildAgeAdoptionWriterGeneration).IsEqualTo(command.ExpectedChildAgeAdoptionWriterGeneration);
        await Assert.That(copy.SourceId).IsEqualTo(command.SourceId);
        await Assert.That(copy.ValidUntil).IsEqualTo(command.ValidUntil);
        await Assert.That(copy.Procedure.Purpose).IsEqualTo(command.Procedure.Purpose);
        await Assert.That(copy.Procedure.Applicability).IsEqualTo(command.Procedure.Applicability);
        await Assert.That(copy.Procedure.PreliminaryFields.SequenceEqual(command.Procedure.PreliminaryFields)).IsTrue();
        await Assert.That(copy.Procedure.NormalFields.SequenceEqual(command.Procedure.NormalFields)).IsTrue();
        await Assert.That(copy.Procedure.GuardianRelationshipReviewGrounds).IsEqualTo(command.Procedure.GuardianRelationshipReviewGrounds);
        await Assert.That(copy.Procedure.ManualIssuerReviewGrounds).IsEqualTo(command.Procedure.ManualIssuerReviewGrounds);
        await Assert.That(copy.Procedure.AllowsNormalField(ModerationProfileNormalFieldKind.Email)).IsFalse();
    }

    [Test]
    public async Task GuardianRelationshipAndConsentRemainDistinctWireCoordinates()
    {
        var command = (RecordModerationProfileCollectionProcedureCommand)Command("record");
        var procedure = new ModerationProfileCollectionProcedure(ModerationIndividualPurpose.ProfileCompletion,
            ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment,
            [ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration,
                ModerationProfilePreliminaryFieldKind.AccountEligibilityReference,
                ModerationProfilePreliminaryFieldKind.SubjectConsentReference,
                ModerationProfilePreliminaryFieldKind.GuardianRelationshipReference,
                ModerationProfilePreliminaryFieldKind.GuardianConsentReference],
            [ModerationProfileNormalFieldKind.DateOfBirth], command.Procedure.GuardianRelationshipReviewGrounds,
            command.Procedure.ManualIssuerReviewGrounds);
        var payload = JsonSerializer.SerializeToElement(command with { Procedure = procedure }, Options);
        var fields = payload.GetProperty("procedure").GetProperty("preliminaryFields")
            .EnumerateArray().Select(value => value.GetString()).ToArray();
        await Assert.That(fields.Contains(nameof(ModerationProfilePreliminaryFieldKind.GuardianRelationshipReference))).IsTrue();
        await Assert.That(fields.Contains(nameof(ModerationProfilePreliminaryFieldKind.GuardianConsentReference))).IsTrue();
        var copy = payload.Deserialize<RecordModerationProfileCollectionProcedureCommand>(Options)!;
        await Assert.That(copy.Procedure.PreliminaryFields.Count).IsEqualTo(5);
        await Assert.That(copy.Procedure.PreliminaryFields.SequenceEqual(procedure.PreliminaryFields)).IsTrue();
    }

    [Test]
    public async Task MaximumEscapedProcedureCriteriaFitTheDeclaredRequestCeiling()
    {
        var command = (RecordModerationProfileCollectionProcedureCommand)Command("record");
        var limit = ((IRequestSizeLimitMetadata)typeof(ModerationProfileCollectionProceduresController)
            .GetCustomAttribute<RequestSizeLimitAttribute>()!).MaxRequestBodySize;
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(command, Options).LongLength <= limit).IsTrue();
    }

    [Test]
    public async Task WithdrawalRouteCannotBeReplacedByValidBodyAdoptionIntent()
    {
        var controller = new ModerationProfileCollectionProceduresController(null!, null!,
            new WithdrawModerationProfileCollectionProcedureCommandHandler(null!))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        var result = await controller.Withdraw(Guid.Empty,
            (WithdrawModerationProfileCollectionProcedureCommand)Command("withdraw"), CancellationToken.None);
        var failure = (ObjectResult)result.Result!;
        await Assert.That(failure.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(failure.Value is ProblemDetails).IsTrue();
    }

    private static ModerationQualificationCommand Command(string kind)
    {
        var target = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var grounds = new string('\u4e00', 2048);
        return kind switch
        {
            "record" => new RecordModerationProfileCollectionProcedureCommand
            {
                OperationId = Guid.CreateVersion7(), Target = target, ExpectedQualificationRevision = 3,
                SourceId = Guid.CreateVersion7(), ChildAgeSourceId = Guid.CreateVersion7(),
                ExpectedChildAgeSourceRevision = 1, ExpectedChildAgeSourceWriterGeneration = 1,
                ChildAgeAdoptionId = Guid.CreateVersion7(), ExpectedChildAgeAdoptionRevision = 1,
                ExpectedChildAgeAdoptionWriterGeneration = 2,
                Procedure = new(ModerationIndividualPurpose.ProfileCompletion,
                    ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment,
                    [ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration,
                        ModerationProfilePreliminaryFieldKind.SubjectConsentReference],
                    [ModerationProfileNormalFieldKind.DateOfBirth, ModerationProfileNormalFieldKind.FirstName,
                        ModerationProfileNormalFieldKind.LastName], grounds, grounds),
                ValidUntil = new(2026, 10, 14, 12, 5, 0, TimeSpan.Zero)
            },
            "adopt" => new AdoptModerationProfileCollectionProcedureCommand
            {
                OperationId = Guid.CreateVersion7(), Target = target, ExpectedQualificationRevision = 4,
                SourceId = Guid.CreateVersion7(), ExpectedSourceRevision = 1, ExpectedSourceWriterGeneration = 3,
                AdoptionId = Guid.CreateVersion7(), ValidUntil = new(2026, 10, 14, 12, 5, 0, TimeSpan.Zero)
            },
            "withdraw" => new WithdrawModerationProfileCollectionProcedureCommand
            {
                OperationId = Guid.CreateVersion7(), Target = target, ExpectedQualificationRevision = 5,
                AdoptionId = Guid.CreateVersion7(), ExpectedAdoptionRevision = 1
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
