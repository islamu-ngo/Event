using System.Reflection;
using System.Text.Json;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Serialization;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationFindingsControllerContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ModerationAuthorityScopeJsonConverter(), new ModerationRuleReferenceJsonConverter() }
    };

    [Test]
    [Arguments("finalize", "adjudicatorHumanId")]
    [Arguments("finalize", "isApproved")]
    [Arguments("finalize", "responseOpportunitySatisfied")]
    [Arguments("reverse", "reversedByHumanId")]
    [Arguments("reverse", "isAdministrator")]
    [Arguments("reverse", "$type")]
    public async Task CallerAuthorityAssertionsCannotBind(string operation, string member)
    {
        ModerationQualificationCommand command = operation == "finalize" ? Finalization() : Reversal();
        var document = JsonSerializer.SerializeToNode(command, command.GetType(), Options)!.AsObject();
        document[member] = "caller assertion";
        await Assert.That(() => JsonSerializer.Deserialize(document.ToJsonString(), command.GetType(), Options))
            .Throws<JsonException>();
    }

    [Test]
    [Arguments("finalize", "expectedCaseRevision")]
    [Arguments("finalize", "expectedPolicyRevision")]
    [Arguments("finalize", "evidenceId")]
    [Arguments("reverse", "expectedFindingRevision")]
    [Arguments("reverse", "reversalReferenceId")]
    public async Task MissingNativeProvenanceCannotBind(string operation, string member)
    {
        ModerationQualificationCommand command = operation == "finalize" ? Finalization() : Reversal();
        var document = JsonSerializer.SerializeToNode(command, command.GetType(), Options)!.AsObject();
        document.Remove(member);
        await Assert.That(() => JsonSerializer.Deserialize(document.ToJsonString(), command.GetType(), Options))
            .Throws<JsonException>();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task FinalizationUsesBothRouteIdentitiesBeforeNativeAcquisition(bool invalidCase)
    {
        var command = Finalization();
        var controller = Controller();
        var action = await controller.FinalizeFinding(invalidCase ? Guid.Empty : command.CaseId,
            invalidCase ? command.FindingId : Guid.Empty, command, CancellationToken.None);
        await AssertInvalid(action);
        await Assert.That(command.CaseId).IsNotEqualTo(Guid.Empty);
        await Assert.That(command.FindingId).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task ReversalUsesRouteIdentityWithoutMutatingCallerIntent()
    {
        var command = Reversal();
        await AssertInvalid(await Controller().ReverseFinding(Guid.Empty, command, CancellationToken.None));
        await Assert.That(command.FindingId).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    [Arguments(nameof(ModerationFindingsController.FinalizeFinding),
        "cases/{caseId:guid}/findings/{findingId:guid}/finalize", RouteNames.FinalizeModerationFinding)]
    [Arguments(nameof(ModerationFindingsController.ReverseFinding),
        "findings/{findingId:guid}/reverse", RouteNames.ReverseModerationFinding)]
    public async Task FindingWritesRemainAuthenticatedBoundedAndNoncached(string action, string template, string routeName)
    {
        var type = typeof(ModerationFindingsController);
        var method = type.GetMethod(action)!;
        await Assert.That(type.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(type.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo("write");
        await Assert.That(((IRequestSizeLimitMetadata?)type.GetCustomAttribute<RequestSizeLimitAttribute>())?.MaxRequestBodySize)
            .IsEqualTo(64L * 1024);
        await Assert.That(type.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(type.GetCustomAttribute<OutputCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Name).IsEqualTo(routeName);
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Template).IsEqualTo(template);
    }

    private static ModerationFindingsController Controller() => new(
        new FinalizeModerationFindingCommandHandler(null!), new ReverseModerationFindingCommandHandler(null!))
    {
        ControllerContext = new() { HttpContext = new DefaultHttpContext() }
    };

    private static async Task AssertInvalid(ActionResult<ModerationQualificationResponse> action)
    {
        var failure = (ObjectResult)action.Result!;
        await Assert.That(failure.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(failure.Value is ProblemDetails).IsTrue();
    }

    private static FinalizeModerationFindingCommand Finalization()
    {
        var scope = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7());
        return new()
        {
            OperationId = Guid.CreateVersion7(),
            Target = scope,
            ExpectedQualificationRevision = 7,
            FindingId = Guid.CreateVersion7(),
            CaseId = Guid.CreateVersion7(),
            ExpectedCaseRevision = 1,
            PolicyId = Guid.CreateVersion7(),
            ExpectedPolicyRevision = 1,
            Rule = ModerationRuleReference.Create(scope.Id, "classification-rule", 1),
            Reason = ModerationRuleReference.Create(scope.Id, "classification-reason", 1),
            EvidenceId = Guid.CreateVersion7(),
            Justification = "The independent reviewer retains the qualified finding."
        };
    }

    private static ReverseModerationFindingCommand Reversal() => new()
    {
        OperationId = Guid.CreateVersion7(),
        Target = Finalization().Target,
        ExpectedQualificationRevision = 8,
        FindingId = Guid.CreateVersion7(),
        ExpectedFindingRevision = 1,
        ReversalReferenceId = Guid.CreateVersion7(),
        Justification = "The independent correction preserves the original finding provenance."
    };
}
