using System.Collections.Immutable;
using Explore.API.Controllers;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationPolicyPublicationControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Deadline = Now.AddHours(1);

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ValidBodyCannotReplaceInvalidRoutePolicy(bool preparation, bool wrongVersion)
    {
        var request = Request();
        var result = await ExecuteAsync(preparation, wrongVersion ? Guid.NewGuid() : Guid.Empty,
            request, RejectUnexpectedWrite());
        await AssertValidationAsync(result);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task MalformedNestedIntentReachesManualValidation(bool preparation, bool missingRequest)
    {
        var request = missingRequest ? null! : Request() with { Proposed = null! };
        var result = await ExecuteAsync(preparation, Guid.CreateVersion7(), request, RejectUnexpectedWrite());
        await AssertValidationAsync(result);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RouteSubstitutionPreservesEveryOtherImmutableIntentAndCancellation(bool preparation)
    {
        var request = Request() with
        {
            ExpectedPublicationId = Guid.CreateVersion7(),
            Proposed = Request().Proposed with { ExpectedRevision = 3 }
        };
        var original = request.Proposed;
        var routePolicy = Guid.CreateVersion7();
        using var cancellation = new CancellationTokenSource();
        ModerationPolicyPublicationRequest? received = null;
        CancellationToken receivedCancellation = default;
        DateTimeOffset? receivedDeadline = null;
        var repository = Substitute.For<IModerationPolicyPublicationRepository>();
        repository.PublishAsync(Arg.Any<ModerationPolicyPublicationRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                received = call.Arg<ModerationPolicyPublicationRequest>();
                receivedCancellation = call.Arg<CancellationToken>();
                return new ModerationPolicyPublicationResult(ModerationPolicyPublicationOutcome.Unavailable);
            });
        repository.PrepareAsync(Arg.Any<ModerationPolicyPublicationRequest>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                received = call.Arg<ModerationPolicyPublicationRequest>();
                receivedCancellation = call.Arg<CancellationToken>();
                receivedDeadline = call.Arg<DateTimeOffset>();
                return new ModerationPolicyPublicationCandidateResult(ModerationPolicyPublicationOutcome.Unavailable);
            });

        var result = await ExecuteAsync(preparation, routePolicy, request, repository, cancellation.Token);

        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        await Assert.That(received).IsEqualTo(request with { Proposed = original with { PolicyId = routePolicy } });
        await Assert.That(ReferenceEquals(request.Proposed, original)).IsTrue();
        await Assert.That(request.Proposed.PolicyId == routePolicy).IsFalse();
        await Assert.That(receivedCancellation).IsEqualTo(cancellation.Token);
        await Assert.That(receivedDeadline).IsEqualTo(preparation ? Deadline : null);
    }

    [Test]
    [Arguments(false, ModerationPolicyPublicationOutcome.Unauthenticated, StatusCodes.Status401Unauthorized)]
    [Arguments(true, ModerationPolicyPublicationOutcome.Unauthenticated, StatusCodes.Status401Unauthorized)]
    [Arguments(false, ModerationPolicyPublicationOutcome.Denied, StatusCodes.Status403Forbidden)]
    [Arguments(true, ModerationPolicyPublicationOutcome.Denied, StatusCodes.Status403Forbidden)]
    [Arguments(false, ModerationPolicyPublicationOutcome.Conflict, StatusCodes.Status409Conflict)]
    [Arguments(true, ModerationPolicyPublicationOutcome.Conflict, StatusCodes.Status409Conflict)]
    [Arguments(false, ModerationPolicyPublicationOutcome.Invalid, StatusCodes.Status400BadRequest)]
    [Arguments(true, ModerationPolicyPublicationOutcome.Invalid, StatusCodes.Status400BadRequest)]
    [Arguments(false, ModerationPolicyPublicationOutcome.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    [Arguments(true, ModerationPolicyPublicationOutcome.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    [Arguments(false, ModerationPolicyPublicationOutcome.Known, StatusCodes.Status503ServiceUnavailable)]
    [Arguments(true, ModerationPolicyPublicationOutcome.Known, StatusCodes.Status503ServiceUnavailable)]
    public async Task NativeFailuresAndMissingReceiptsRemainDistinctProblems(
        bool preparation, ModerationPolicyPublicationOutcome outcome, int status)
    {
        var repository = Substitute.For<IModerationPolicyPublicationRepository>();
        repository.PublishAsync(Arg.Any<ModerationPolicyPublicationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationPolicyPublicationResult(outcome));
        repository.PrepareAsync(Arg.Any<ModerationPolicyPublicationRequest>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(new ModerationPolicyPublicationCandidateResult(outcome));
        var result = await ExecuteAsync(preparation, Guid.CreateVersion7(), Request(), repository);

        await Assert.That(result.StatusCode).IsEqualTo(status);
        await Assert.That(result.Value is ProblemDetails).IsTrue();
        await Assert.That(result.Value is BaseCommandResponse<Guid>).IsFalse();
        if (status == StatusCodes.Status400BadRequest)
            await AssertValidationAsync(result);
        if (status == StatusCodes.Status503ServiceUnavailable)
            await Assert.That(((ProblemDetails)result.Value!).Extensions["code"]).IsEqualTo(preparation
                ? "ModerationPublicationPreparationUnavailable" : "ModerationPublicationUnavailable");
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task PreparationRejectsDraftOrNonFiniteUtcDeadlineBeforeNativeWrite(int invalid)
    {
        var request = Request();
        var deadline = Deadline;
        if (invalid == 0)
            request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Draft } };
        else
            deadline = invalid == 1 ? Deadline.ToOffset(TimeSpan.FromHours(1)) : DateTimeOffset.MaxValue;
        var controller = PreparationController(RejectUnexpectedWrite());
        var result = await controller.Prepare(Guid.CreateVersion7(), new(request, deadline), default);
        await AssertValidationAsync((ObjectResult)result.Result!);
    }

    [Test]
    public async Task PublishedDraftReturnsExactPublicationMetadataWithoutCurrentAuthority()
    {
        var request = Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Draft } };
        var publication = Snapshot(request);
        var repository = Substitute.For<IModerationPolicyPublicationRepository>();
        repository.PublishAsync(Arg.Any<ModerationPolicyPublicationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationPolicyPublicationResult(ModerationPolicyPublicationOutcome.Known, publication));
        var result = await PublicationController(repository).Publish(publication.PolicyId, new(request), default);
        var response = (result.Result as OkObjectResult)?.Value as ModerationPolicyPublicationResponse;

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.IsSuccess).IsTrue();
        await Assert.That(response.Id).IsEqualTo(publication.PolicyId);
        await Assert.That(response.PublicationId).IsEqualTo(publication.Id);
        await Assert.That(response.Revision).IsEqualTo(1);
        await Assert.That(response.PublishedActive).IsEqualTo(false);
    }

    [Test]
    public async Task PreparationReturnsReservedIdentityAndDeadlineNotPublicationResponse()
    {
        var request = Request();
        var snapshot = Snapshot(request);
        var candidate = ModerationPolicyPublicationCandidate.Prepare(snapshot, Guid.CreateVersion7(),
            request.OperationId, snapshot.RequestDigest, new string('b', 64), snapshot.PrincipalId, Now, Deadline);
        var repository = Substitute.For<IModerationPolicyPublicationRepository>();
        repository.PrepareAsync(Arg.Any<ModerationPolicyPublicationRequest>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(new ModerationPolicyPublicationCandidateResult(ModerationPolicyPublicationOutcome.Known, candidate));
        var result = await PreparationController(repository).Prepare(candidate.PolicyId, new(request, Deadline), default);
        var value = (result.Result as OkObjectResult)?.Value;
        var response = value as ModerationPolicyPublicationCandidateResponse;

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.IsSuccess).IsTrue();
        await Assert.That(response.Id).IsEqualTo(candidate.Id);
        await Assert.That(response.Revision).IsEqualTo(1);
        await Assert.That(response.ValidUntil).IsEqualTo(Deadline);
        await Assert.That(value is ModerationPolicyPublicationResponse).IsFalse();
        await Assert.That(snapshot.PublishedActive).IsFalse();
        await Assert.That(snapshot.AdoptedRevision).IsNull();
    }

    private static async Task<ObjectResult> ExecuteAsync(bool preparation, Guid policyId,
        ModerationPolicyPublicationRequest request, IModerationPolicyPublicationRepository repository,
        CancellationToken cancellationToken = default)
    {
        if (preparation)
        {
            var result = await PreparationController(repository).Prepare(policyId, new(request, Deadline), cancellationToken);
            return (ObjectResult)result.Result!;
        }
        var publication = await PublicationController(repository).Publish(policyId, new(request), cancellationToken);
        return (ObjectResult)publication.Result!;
    }

    private static ModerationPolicyPublicationController PublicationController(
        IModerationPolicyPublicationRepository repository) => new(new PublishModerationPolicyCommandHandler(repository))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };

    private static ModerationPolicyPublicationPreparationController PreparationController(
        IModerationPolicyPublicationRepository repository) => new(new PrepareModerationPolicyPublicationCommandHandler(repository))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };

    private static IModerationPolicyPublicationRepository RejectUnexpectedWrite()
    {
        var repository = Substitute.For<IModerationPolicyPublicationRepository>();
        repository.PublishAsync(Arg.Any<ModerationPolicyPublicationRequest>(), Arg.Any<CancellationToken>())
            .Returns<ModerationPolicyPublicationResult>(_ =>
                throw new InvalidOperationException("Invalid transport intent must not reach publication."));
        repository.PrepareAsync(Arg.Any<ModerationPolicyPublicationRequest>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns<ModerationPolicyPublicationCandidateResult>(_ =>
                throw new InvalidOperationException("Invalid transport intent must not reach preparation."));
        return repository;
    }

    private static async Task AssertValidationAsync(ObjectResult result)
    {
        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(result.Value is ValidationProblemDetails validation
            && validation.Errors.ContainsKey("request")).IsTrue();
    }

    private static ModerationPolicyPublicationRequest Request()
    {
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var registry = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        var reference = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG05);
        var response = reference.Responses[0];
        ModerationRuleReference Rule(string code) => ModerationRuleReference.Create(instance.Id, code, 1);
        var rule = Rule("publication.rule");
        var reason = Rule("publication.reason");
        var classification = Rule("publication.class");
        var profile = new ModerationPolicyProfileDefinition(Guid.CreateVersion7(), rule, reason,
            reference.Id, reference.Revision, response.Grade, [ModerationScopeKind.Tenant],
            [ModerationSubjectKind.Human], reference.Effects.ToImmutableArray(), reference.Bundle,
            reference.ProtectionPurpose, reference.Strategies.ToImmutableArray(),
            new(response.Timing.Kind, response.Timing.Duration), response.RequiredApprovals, 6, reference.Recognition);
        var proposed = new ModerationPolicyProposedState(Guid.CreateVersion7(), 0, Guid.CreateVersion7(),
            instance, ModerationReferenceCatalogue.Revision, registry.Revision, registry.Fingerprint,
            ModerationPolicyRequestedState.Active, [ModerationNativeMode.Embedded],
            [new(classification, "Publication conduct", reference.Ground, reference.Basis)],
            [new(reason, "Qualified publication finding", classification)], [profile], [rule]);
        return new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1,
            ModerationPolicyPublicationProjection.WriterGeneration, null, proposed);
    }

    private static ModerationPolicyPublication Snapshot(ModerationPolicyPublicationRequest request) => new()
    {
        Id = request.Proposed.PublicationId,
        PolicyId = request.Proposed.PolicyId,
        Revision = 1,
        AdoptedRevision = null,
        PreviousPublicationId = null,
        HostBindingId = request.ExpectedHostBindingId,
        TrustDomainId = request.Proposed.Jurisdiction.Parent!.Parent!.Id,
        SolutionId = request.Proposed.Jurisdiction.Parent.Id,
        InstanceId = request.Proposed.Jurisdiction.Id,
        JurisdictionTenantId = null,
        TenantId = Guid.CreateVersion7(),
        PrincipalId = Guid.CreateVersion7(),
        OperationId = request.OperationId,
        RequestDigest = new string('a', 64),
        DeclaredStateDigest = new string('b', 64),
        AuthoritySourceId = request.ExpectedAuthoritySourceId,
        AuthorityRevision = 1,
        QualificationRevision = 2,
        WriterGeneration = request.ExpectedWriterGeneration,
        CatalogueRevision = request.Proposed.CatalogueRevision,
        InventoryRevision = request.Proposed.InventoryRevision,
        InventoryFingerprint = request.Proposed.InventoryFingerprint,
        PublishedActive = false,
        PublishedAt = Now,
        EvidenceValidUntil = null,
        MaximumFiniteDurationTicks = TimeSpan.FromDays(1).Ticks,
        MaximumReviewDelayTicks = TimeSpan.FromDays(1).Ticks,
        MinimumApprovals = 0,
        MaximumApprovals = 2,
        MinimumContestationCalendarMonths = 6,
        Classes = [],
        Reasons = [],
        Profiles = [],
        Modes = [],
        Evidence = []
    };
}
