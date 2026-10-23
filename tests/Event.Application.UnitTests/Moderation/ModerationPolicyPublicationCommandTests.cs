using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationPolicyPublicationCommandTests
{
    [Test]
    [Arguments("operation")]
    [Arguments("host")]
    [Arguments("authority")]
    [Arguments("epoch")]
    [Arguments("generation")]
    [Arguments("policy")]
    [Arguments("publication")]
    [Arguments("missing_previous")]
    [Arguments("initial_previous")]
    [Arguments("revision")]
    [Arguments("scope")]
    [Arguments("state")]
    [Arguments("modes")]
    [Arguments("classes")]
    [Arguments("reasons")]
    [Arguments("profiles")]
    [Arguments("oversized_profiles")]
    [Arguments("oversized_meaning")]
    [Arguments("fingerprint")]
    public async Task Publication_intent_rejects_missing_coordinates_and_incomplete_bounded_state(string change)
    {
        var request = Request();
        var proposed = request.Proposed;
        request = change switch
        {
            "operation" => request with { OperationId = Guid.Empty },
            "host" => request with { ExpectedHostBindingId = Guid.Empty },
            "authority" => request with { ExpectedAuthoritySourceId = Guid.Empty },
            "epoch" => request with { ExpectedQualificationRevision = 0 },
            "generation" => request with { ExpectedWriterGeneration = "unrecognized" },
            "policy" => request with { Proposed = proposed with { PolicyId = Guid.Empty } },
            "publication" => request with { Proposed = proposed with { PublicationId = Guid.Empty } },
            "missing_previous" => request with { Proposed = proposed with { ExpectedRevision = 1 } },
            "initial_previous" => request with { ExpectedPublicationId = Guid.CreateVersion7() },
            "revision" => request with { Proposed = proposed with { ExpectedRevision = long.MaxValue } },
            "scope" => request with { Proposed = proposed with { Jurisdiction = proposed.Jurisdiction.Parent! } },
            "state" => request with { Proposed = proposed with { RequestedState = (ModerationPolicyRequestedState)999 } },
            "modes" => request with { Proposed = proposed with { Modes = [] } },
            "classes" => request with { Proposed = proposed with { Classes = [] } },
            "reasons" => request with { Proposed = proposed with { Reasons = [] } },
            "profiles" => request with { Proposed = proposed with { Profiles = [] } },
            "oversized_profiles" => request with
            {
                Proposed = proposed with
                { Profiles = Enumerable.Repeat(proposed.Profiles[0], 257).ToImmutableArray() }
            },
            "oversized_meaning" => request with
            {
                Proposed = proposed with
                { Classes = [proposed.Classes[0] with { Meaning = new string('a', 4001) }] }
            },
            "fingerprint" => request with { Proposed = proposed with { InventoryFingerprint = new string('a', 65) } },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        var validation = await new ModerationPolicyPublicationCommandValidator()
            .ValidateAsync(new PublishModerationPolicyCommand(request));
        await Assert.That(validation.IsValid).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Complete_initial_and_replacement_intent_keep_native_history_qualification_in_the_writer(bool replacement)
    {
        var request = Request();
        if (replacement)
            request = request with
            {
                ExpectedPublicationId = Guid.CreateVersion7(),
                Proposed = request.Proposed with { ExpectedRevision = 2 }
            };
        var validation = await new ModerationPolicyPublicationCommandValidator()
            .ValidateAsync(new PublishModerationPolicyCommand(request));
        await Assert.That(validation.IsValid).IsTrue();
    }

    [Test]
    [Arguments(ModerationPolicyPublicationOutcome.Unauthenticated, FailureCodes.AuthenticationRequired)]
    [Arguments(ModerationPolicyPublicationOutcome.Denied, FailureCodes.AdminRequired)]
    [Arguments(ModerationPolicyPublicationOutcome.Conflict, FailureCodes.ConcurrencyConflict)]
    public async Task PreparationKeepsNativeFailureIdentityWithoutPublishingARevision(
        ModerationPolicyPublicationOutcome outcome, string failureCode)
    {
        var response = ModerationPolicyPublicationCandidateResponse.FromOutcome(Guid.CreateVersion7(), new(outcome));
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo(failureCode);
        await Assert.That(response.Revision).IsNull();
        await Assert.That(response.ValidUntil).IsNull();
    }

    [Test]
    public async Task Missing_active_authority_returns_no_publication_or_activation_state()
    {
        var response = ModerationPolicyPublicationResponse.FromOutcome(
            Guid.CreateVersion7(), new(ModerationPolicyPublicationOutcome.Unavailable));
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("ModerationPublicationUnavailable");
        await Assert.That(response.PublicationId is null).IsTrue();
        await Assert.That(response.Revision is null).IsTrue();
        await Assert.That(response.PublishedActive is null).IsTrue();
    }

    [Test]
    [Arguments(ModerationPolicyPublicationOutcome.Unauthenticated, FailureCodes.AuthenticationRequired)]
    [Arguments(ModerationPolicyPublicationOutcome.Denied, FailureCodes.AdminRequired)]
    [Arguments(ModerationPolicyPublicationOutcome.Conflict, FailureCodes.ConcurrencyConflict)]
    public async Task Native_authority_outcomes_remain_distinct(
        ModerationPolicyPublicationOutcome outcome, string failureCode)
    {
        var response = ModerationPolicyPublicationResponse.FromOutcome(Guid.CreateVersion7(), new(outcome));
        await Assert.That(response.FailureCode).IsEqualTo(failureCode);
        await Assert.That(response.PublicationId is null).IsTrue();
    }

    private static ModerationPolicyPublicationRequest Request()
    {
        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7());
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
            ModerationPolicyRequestedState.Draft, [ModerationNativeMode.Embedded],
            [new(classification, "Publication conduct", reference.Ground, reference.Basis)],
            [new(reason, "Qualified publication finding", classification)], [profile], [rule]);
        return new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1,
            ModerationPolicyPublicationProjection.WriterGeneration, null, proposed);
    }
}
