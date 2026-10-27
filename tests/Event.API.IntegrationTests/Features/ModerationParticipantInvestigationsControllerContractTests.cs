using System.Reflection;
using System.Text.Json;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Serialization;
using Explore.Application.Contracts.Persistence;
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

public sealed class ModerationParticipantInvestigationsControllerContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ModerationAuthorityScopeJsonConverter(), new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    [Test]
    public async Task CaptureIsAnAuthenticatedBoundedUncachedClosedNamedWrite()
    {
        var controller = typeof(ModerationParticipantInvestigationsController);
        var method = controller.GetMethod(nameof(ModerationParticipantInvestigationsController.Record))!;
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(controller.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(controller.GetCustomAttribute<RouteAttribute>()?.Template)
            .IsEqualTo("api/moderation/participant-investigations");
        await Assert.That(controller.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo("write");
        await Assert.That(((IRequestSizeLimitMetadata?)controller.GetCustomAttribute<RequestSizeLimitAttribute>())?.MaxRequestBodySize)
            .IsEqualTo(32L * 1024);
        await Assert.That(controller.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(controller.GetCustomAttribute<OutputCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Template).IsEqualTo("sources");
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Name)
            .IsEqualTo(RouteNames.RecordModerationParticipantInvestigation);
        await Assert.That(method.GetParameters()[0].ParameterType)
            .IsEqualTo(typeof(RecordModerationParticipantInvestigationCommand));
        await Assert.That(method.GetCustomAttributes<ProducesResponseTypeAttribute>().Any(response =>
            response.StatusCode == 200 && response.Type == typeof(ModerationQualificationResponse))).IsTrue();
    }

    [Test]
    [Arguments("investigatorHumanId")]
    [Arguments("isAuthorized")]
    [Arguments("corroborated")]
    [Arguments("finding")]
    [Arguments("grounds")]
    [Arguments("$type")]
    public async Task UnknownAuthorityOrPolymorphicMembersCannotEnterCapture(string member)
    {
        var document = JsonSerializer.SerializeToNode(Valid(), Options)!.AsObject();
        document[member] = "caller assertion";
        await Assert.That(() => JsonSerializer.Deserialize<RecordModerationParticipantInvestigationCommand>(
            document.ToJsonString(), Options)).Throws<JsonException>();
    }

    [Test]
    [Arguments("readGrantId")]
    [Arguments("expectedCaseRevision")]
    [Arguments("evidenceSourceVersion")]
    [Arguments("expectedSessionGeneration")]
    [Arguments("purgeAt")]
    [Arguments("identificationGrounds")]
    [Arguments("allegedConduct")]
    [Arguments("evidenceProvenance")]
    [Arguments("contradictionsAndCredentialMisuse")]
    public async Task MissingRequiredCaptureMembersCannotBind(string member)
    {
        var document = JsonSerializer.SerializeToNode(Valid(), Options)!.AsObject();
        document.Remove(member);
        await Assert.That(() => JsonSerializer.Deserialize<RecordModerationParticipantInvestigationCommand>(
            document.ToJsonString(), Options)).Throws<JsonException>();
    }

    [Test]
    [Arguments(ModerationQualificationWriteOutcome.Unauthenticated, 401)]
    [Arguments(ModerationQualificationWriteOutcome.Denied, 403)]
    [Arguments(ModerationQualificationWriteOutcome.NotFound, 404)]
    [Arguments(ModerationQualificationWriteOutcome.Conflict, 409)]
    [Arguments(ModerationQualificationWriteOutcome.Unavailable, 503)]
    [Arguments(ModerationQualificationWriteOutcome.Known, 503)]
    public async Task RealHandlerAndControllerMapUnqualifiedNativeOutcomesWithoutPlaintext(
        ModerationQualificationWriteOutcome outcome, int status)
    {
        // This seam supplies only a native outcome, never assessor authority or protected custody.
        var controller = new ModerationParticipantInvestigationsController(
            new RecordModerationParticipantInvestigationCommandHandler(new OutcomeRepository(outcome)))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        var command = Valid();
        var action = await controller.Record(command, CancellationToken.None);
        var result = (ObjectResult)action.Result!;
        await Assert.That(result.StatusCode).IsEqualTo(status);
        await Assert.That(result.Value is ProblemDetails).IsTrue();
        var json = JsonSerializer.Serialize(result.Value, Options);
        await Assert.That(json.Contains(command.IdentificationGrounds)).IsFalse();
        await Assert.That(json.Contains(command.AllegedConduct)).IsFalse();
        await Assert.That(json.Contains(command.EvidenceProvenance)).IsFalse();
        await Assert.That(json.Contains(command.ContradictionsAndCredentialMisuse)).IsFalse();
        await Assert.That(json.Contains("assessmentDigest", StringComparison.OrdinalIgnoreCase)).IsFalse();
    }

    [Test]
    public async Task InvalidGroundsBecomeGenericValidationProblemBeforeRepositoryAccess()
    {
        var controller = new ModerationParticipantInvestigationsController(
            new RecordModerationParticipantInvestigationCommandHandler(null!))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        var command = Valid() with { AllegedConduct = new string('x', 2049) };
        var action = await controller.Record(command, CancellationToken.None);
        var result = (ObjectResult)action.Result!;
        await Assert.That(result.StatusCode).IsEqualTo(400);
        await Assert.That(result.Value is ProblemDetails).IsTrue();
        await Assert.That(JsonSerializer.Serialize(result.Value, Options).Contains(command.AllegedConduct)).IsFalse();
    }

    [Test]
    public async Task AcceptedNativeReceiptReturnsOnlySourceIdentityAndRevisions()
    {
        var command = Valid();
        var receipt = ModerationQualificationMutationReceipt.Complete(command.OperationId, new string('a', 64),
            Guid.CreateVersion7(), command.Target.TrustDomainId, command.Target.Id,
            ModerationQualificationSourceKind.ParticipantIncidentResponsibilitySource,
            command.SourceId, Guid.CreateVersion7(), 1, 5, command.ValidUntil);
        var controller = new ModerationParticipantInvestigationsController(
            new RecordModerationParticipantInvestigationCommandHandler(
                new OutcomeRepository(ModerationQualificationWriteOutcome.Known, receipt)));
        var action = await controller.Record(command, CancellationToken.None);
        var result = (OkObjectResult)action.Result!;
        var response = (ModerationQualificationResponse)result.Value!;
        await Assert.That(response.Id).IsEqualTo(command.SourceId);
        await Assert.That(response.Revision).IsEqualTo(1L);
        await Assert.That(response.QualificationRevision).IsEqualTo(5L);
        var json = JsonSerializer.SerializeToNode(response, Options)!.AsObject();
        await Assert.That(json.Any(field => field.Key.Contains("ground", StringComparison.OrdinalIgnoreCase)
            || field.Key.Contains("digest", StringComparison.OrdinalIgnoreCase)
            || field.Key.Contains("case", StringComparison.OrdinalIgnoreCase)
            || field.Key.Contains("assessment", StringComparison.OrdinalIgnoreCase))).IsFalse();
        await Assert.That(json.ToJsonString().Contains(command.IdentificationGrounds)).IsFalse();
        await Assert.That(json.ToJsonString().Contains(command.AllegedConduct)).IsFalse();
        await Assert.That(json.ToJsonString().Contains(command.EvidenceProvenance)).IsFalse();
        await Assert.That(json.ToJsonString().Contains(command.ContradictionsAndCredentialMisuse)).IsFalse();
    }

    private sealed class OutcomeRepository(ModerationQualificationWriteOutcome outcome,
        ModerationQualificationMutationReceipt? receipt = null) : IModerationQualificationRepository
    {
        public Task<ModerationQualificationWriteResult> ExecuteAsync(
            ModerationQualificationMutation mutation, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModerationQualificationWriteResult(outcome, receipt));
    }

    private static RecordModerationParticipantInvestigationCommand Valid() => new()
    {
        OperationId = Guid.CreateVersion7(),
        Target = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7()),
        ExpectedQualificationRevision = 4,
        SourceId = Guid.CreateVersion7(),
        OccurrenceCaseId = Guid.CreateVersion7(),
        ExpectedCaseRevision = 3,
        EvidenceCaseRevision = 2,
        ReadGrantId = Guid.CreateVersion7(),
        EvidenceSourceId = Guid.CreateVersion7(),
        EvidenceSourceVersion = Guid.NewGuid(),
        SubjectHumanId = Guid.CreateVersion7(),
        SessionId = Guid.CreateVersion7(),
        ExpectedSessionGeneration = Guid.NewGuid(),
        IncidentWindowFrom = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero),
        IncidentWindowUntil = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero),
        ValidUntil = new(2026, 10, 13, 11, 0, 0, TimeSpan.Zero),
        PurgeAt = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero),
        IdentificationGrounds = "Protected incident-specific identification assessment.",
        AllegedConduct = "Protected allegation assessed independently, not established guilt.",
        EvidenceProvenance = "Protected original evidence provenance assessment.",
        ContradictionsAndCredentialMisuse = "Protected contradictions and possible credential misuse assessment."
    };
}
