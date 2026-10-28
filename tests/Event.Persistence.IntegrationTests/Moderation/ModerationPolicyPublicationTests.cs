using System.Collections.Immutable;
using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Domain;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>
/// Native writer regressions, authored and unrun. Fixture evidence exercises comparison/rollback,
/// never establishes deployed consumer conformance or approves a live moderation profile.
/// </summary>
public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Inactive_publication_roundtrips_complete_meanings_and_protective_purposes_without_evidence()
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request(ModerationReferenceProfileId.CFG19, ModerationReferenceProfileId.CFG20);
        var written = await database.PublishAsync(request);
        await Assert.That(written.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var selected = await database.Writer(context).AcquireSelectedAsync(transaction.TransactionId,
            database.HostId, database.Scope, request.Proposed.PolicyId, false);
        await Assert.That(selected is not null).IsTrue();
        await Assert.That(selected!.PublishedActive).IsFalse();
        await Assert.That(selected.Evidence.Count).IsEqualTo(0);
        await Assert.That(selected.Profiles.Select(profile => profile.ProtectionPurpose).ToHashSet()
            .SetEquals([ModerationProtectionPurpose.EntitlementIntegrity, ModerationProtectionPurpose.CredentialRecovery])).IsTrue();
        var restored = ModerationPolicyPublicationProjection.Restore(selected, database.Inventory)!;
        await Assert.That(restored.Proposed.Classes.Select(row => row.Meaning).ToHashSet()
            .SetEquals(request.Proposed.Classes.Select(row => row.Meaning))).IsTrue();
        await Assert.That(restored.Proposed.Reasons.Select(row => row.Meaning).ToHashSet()
            .SetEquals(request.Proposed.Reasons.Select(row => row.Meaning))).IsTrue();
        var integrity = restored.Profiles.Single(profile => profile.Definition.ReferenceProfile == ModerationReferenceProfileId.CFG19);
        var recovery = restored.Profiles.Single(profile => profile.Definition.ReferenceProfile == ModerationReferenceProfileId.CFG20);
        await Assert.That(integrity.Consequences[0].Credentials).IsEqualTo(ModerationCredentialMode.InvalidateProvedBadEntitlementsOnly);
        await Assert.That(recovery.Consequences[0].Credentials).IsEqualTo(ModerationCredentialMode.VerifyRecoveryBeforeRelease);
        await Assert.That(await database.Writer(context).AcquireSelectedAsync(transaction.TransactionId,
            database.HostId, database.Scope, request.Proposed.PolicyId, true)).IsNull();
    }

    [Test]
    public async Task Complete_pack_retains_all_twenty_seven_definitions_without_certifying_unsupported_profiles()
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request(Enum.GetValues<ModerationReferenceProfileId>());
        // Broad reference definitions may be archived at tenant scope, but are not selected there.
        request = request with { Proposed = request.Proposed with { SupportedProfiles = [] } };
        var result = await database.PublishAsync(request);
        await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await Assert.That(result.Publication!.Profiles.Count).IsEqualTo(27);
        await Assert.That(result.Publication.Profiles.Select(row => row.Bundle).Distinct().Count()).IsEqualTo(10);
        await Assert.That(result.Publication.Profiles.SelectMany(row => row.Plans)
            .Where(row => row.Strategy != 0).Select(row => row.Strategy).Distinct().Count()).IsEqualTo(3);
        await Assert.That(result.Publication.Profiles.Any(row => row.Supported)).IsFalse();
        await Assert.That(result.Publication.PublishedActive).IsFalse();
    }

    [Test]
    [Arguments("meaning")]
    [Arguments("effects")]
    [Arguments("fingerprint")]
    public async Task Unbounded_authoring_state_cannot_bypass_the_native_writer_boundary(string change)
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        var proposed = request.Proposed;
        request = request with
        {
            Proposed = change switch
            {
                "meaning" => proposed with
                { Classes = [proposed.Classes[0] with { Meaning = new string('a', 4001) }] },
                "effects" => proposed with
                {
                    Profiles = [proposed.Profiles[0] with
                { Effects = Enumerable.Repeat(proposed.Profiles[0].Effects[0], 27).ToImmutableArray() }]
                },
                "fingerprint" => proposed with { InventoryFingerprint = new string('a', 65) },
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            }
        };
        await Assert.That((await database.PublishAsync(request)).Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Invalid);
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    public async Task Exact_replay_requalifies_current_human_and_does_not_advance_the_epoch_twice()
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        var first = await database.PublishAsync(request);
        var replay = await database.PublishAsync(request);
        await Assert.That(first.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await Assert.That(replay.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await Assert.That(replay.Publication!.Id).IsEqualTo(first.Publication!.Id);
        await database.AssertCountsAsync(1, 1, 2);
        await using (var context = database.Context())
        {
            context.PlatformUserRoles.Remove(await context.PlatformUserRoles.SingleAsync(row => row.UserId == database.AdminId));
            await context.SaveChangesAsync();
        }
        await Assert.That((await database.PublishAsync(request)).Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Denied);
        await database.AssertCountsAsync(1, 1, 2);
    }

    [Test]
    public async Task Reused_operation_with_changed_complete_state_conflicts_without_a_second_publication()
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        await Assert.That((await database.PublishAsync(request)).Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var changed = request with
        {
            Proposed = request.Proposed with
            {
                Reasons = [request.Proposed.Reasons[0] with { Meaning = "Different semantic reason." }]
            }
        };
        await Assert.That((await database.PublishAsync(changed)).Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Conflict);
        await database.AssertCountsAsync(1, 1, 2);
    }

    [Test]
    public async Task Lost_commit_acknowledgement_replays_the_exact_native_publication_without_duplicate_children()
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        await using (var context = database.Context(new LostCommitAcknowledgement()))
            await Assert.That(async () => await database.Writer(context).PublishAsync(request)).Throws<TimeoutException>();
        var replay = await database.PublishAsync(request);
        await Assert.That(replay.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await Assert.That(replay.Publication!.Id).IsEqualTo(request.Proposed.PublicationId);
        await database.AssertCountsAsync(1, 1, 2);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Changed_or_dropped_historical_meaning_cannot_replace_the_selected_publication(int change)
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        await Assert.That((await database.PublishAsync(request)).Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var next = database.Next(request);
        next = next with
        {
            Proposed = change switch
            {
                0 => next.Proposed with { Classes = [next.Proposed.Classes[0] with { Meaning = "Silently rewritten class." }] },
                1 => next.Proposed with { Reasons = [next.Proposed.Reasons[0] with { Meaning = "Silently rewritten reason." }] },
                _ => next.Proposed with { Profiles = [next.Proposed.Profiles[0] with { Id = Guid.CreateVersion7() }] }
            }
        };
        await Assert.That((await database.PublishAsync(next)).Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Invalid);
        await database.AssertCountsAsync(1, 1, 2);
    }

    [Test]
    public async Task New_semantic_revisions_retain_the_exact_earlier_class_reason_and_profile()
    {
        await using var database = await Database.CreateAsync();
        var first = database.Request();
        await Assert.That((await database.PublishAsync(first)).Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var next = database.Next(first);
        ModerationRuleReference Revised(ModerationRuleReference reference) =>
            ModerationRuleReference.Create(reference.OwnerId, reference.Code, reference.Revision + 1);
        var classification = first.Proposed.Classes[0] with
        { Reference = Revised(first.Proposed.Classes[0].Reference), Meaning = "New explicitly revised meaning." };
        var reason = first.Proposed.Reasons[0] with
        { Reference = Revised(first.Proposed.Reasons[0].Reference), Class = classification.Reference, Meaning = "New explicitly revised reason." };
        var profile = first.Proposed.Profiles[0] with
        { Id = Guid.CreateVersion7(), Rule = Revised(first.Proposed.Profiles[0].Rule), Reason = reason.Reference };
        next = next with
        {
            Proposed = next.Proposed with
            {
                Classes = [first.Proposed.Classes[0], classification],
                Reasons = [first.Proposed.Reasons[0], reason],
                Profiles = [first.Proposed.Profiles[0], profile],
                SupportedProfiles = [profile.Rule]
            }
        };
        var result = await database.PublishAsync(next);
        await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var restored = ModerationPolicyPublicationProjection.Restore(result.Publication!, database.Inventory)!;
        await Assert.That(restored.Proposed.Classes.Single(row => row.Reference.Revision == 1).Meaning)
            .IsEqualTo(first.Proposed.Classes[0].Meaning);
        await Assert.That(restored.Proposed.Reasons.Single(row => row.Reference.Revision == 1).Meaning)
            .IsEqualTo(first.Proposed.Reasons[0].Meaning);
        await Assert.That(restored.Profiles.Single(row => row.Definition.Rule.Revision == 2).Definition.Id).IsEqualTo(profile.Id);
        await Assert.That(restored.Proposed.SupportedProfiles.Single()).IsEqualTo(profile.Rule);
        await database.AssertCountsAsync(2, 1, 3);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task Stale_native_coordinates_cannot_rebind_an_otherwise_valid_publication(int stale)
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        request = stale switch
        {
            0 => request with { ExpectedHostBindingId = Guid.CreateVersion7() },
            1 => request with { ExpectedAuthoritySourceId = Guid.CreateVersion7() },
            2 => request with { ExpectedQualificationRevision = 2 },
            _ => request with { ExpectedPublicationId = Guid.CreateVersion7() }
        };
        await Assert.That((await database.PublishAsync(request)).Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Conflict);
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    public async Task Machine_or_foreign_tenant_cannot_borrow_installed_human_authority()
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        await Assert.That((await database.PublishAsync(request, machine: true)).Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Denied);
        var foreign = request with
        {
            Proposed = request.Proposed with
            {
                Jurisdiction = AuthorityScope.Tenant(database.Scope.Parent!, Guid.CreateVersion7())
            }
        };
        await Assert.That((await database.PublishAsync(foreign)).Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Denied);
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    public async Task Missing_production_evidence_owner_never_turns_implemented_labels_into_active_publication()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        request = await database.PrepareActiveAsync(request);
        await Assert.That((await database.PublishAsync(request)).Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Unavailable);
        await database.AssertCountsAsync(1, 0, 2);
    }

    [Test]
    [Arguments(EvidenceFailure.StaleSource)]
    [Arguments(EvidenceFailure.WrongTransaction)]
    [Arguments(EvidenceFailure.MissingSemanticOwner)]
    [Arguments(EvidenceFailure.MissingConsequenceOwner)]
    [Arguments(EvidenceFailure.MissingNativePrerequisite)]
    [Arguments(EvidenceFailure.NotMeasured)]
    [Arguments(EvidenceFailure.PartialConsumerSet)]
    [Arguments(EvidenceFailure.StaleConsumerGeneration)]
    [Arguments(EvidenceFailure.ExpiredMeasurement)]
    [Arguments(EvidenceFailure.UnboundedMeasurement)]
    [Arguments(EvidenceFailure.NonUtcMeasurement)]
    public async Task Incomplete_or_noncurrent_evidence_rolls_back_the_entire_active_replacement(EvidenceFailure failure)
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var original = database.Request();
        await Assert.That((await database.PublishAsync(original)).Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var active = database.Next(original);
        active = active with { Proposed = active.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        active = await database.PrepareActiveAsync(active);
        await using var context = database.Context();
        var result = await database.Writer(context, new EvidenceOwner(database, context, failure)).PublishAsync(active);
        await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Unavailable);
        await database.AssertCountsAsync(2, 1, 3);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var selection = await reader.ModerationPolicySelections.SingleAsync();
        await Assert.That(selection.LatestPublicationId).IsEqualTo(original.Proposed.PublicationId);
        await Assert.That(selection.ActivePublicationId).IsNull();
    }

    [Test]
    public async Task Complete_fixture_evidence_replaces_active_pointer_while_later_draft_leaves_it_selected()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        request = await database.PrepareActiveAsync(request);
        await using (var context = database.Context())
        {
            var result = await database.Writer(context, new EvidenceOwner(database, context)).PublishAsync(request);
            await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
            await Assert.That(result.Publication!.Evidence.Count > 0).IsTrue();
        }
        var draft = database.Next(request);
        draft = draft with { Proposed = draft.Proposed with { RequestedState = ModerationPolicyRequestedState.Draft } };
        await Assert.That((await database.PublishAsync(draft)).Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await using var transaction = await reader.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var source = new ModerationPolicySelectionSource(reader, database.Writer(reader), database.Inventory);
        var selected = await source.AcquireAsync(transaction.TransactionId, database.HostId, database.Scope,
            request.Proposed.Profiles[0].Rule, 1, true, CancellationToken.None);
        await Assert.That(selected!.Publication.Id).IsEqualTo(request.Proposed.PublicationId);
        await Assert.That(selected.Draft.Proposed.Reasons[0].Meaning).IsEqualTo(request.Proposed.Reasons[0].Meaning);
        var latest = await database.Writer(reader).AcquireSelectedAsync(transaction.TransactionId,
            database.HostId, database.Scope, request.Proposed.PolicyId, false);
        await Assert.That(latest!.Id).IsEqualTo(draft.Proposed.PublicationId);
    }

    [Test]
    public async Task Native_owner_partial_flush_is_rolled_back_when_remaining_qualification_fails()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        request = await database.PrepareActiveAsync(request);
        await using var context = database.Context();
        var result = await database.Writer(context, new EvidenceOwner(database, context, EvidenceFailure.FlushThenReject)).PublishAsync(request);
        await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Unavailable);
        await database.AssertCountsAsync(1, 0, 2);
    }

    [Test]
    public async Task Retained_active_publication_receipts_do_not_replace_current_activation_measurements()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        request = await database.PrepareActiveAsync(request);
        await using (var writer = database.Context())
            await Assert.That((await database.Writer(writer, new EvidenceOwner(database, writer)).PublishAsync(request)).Outcome)
                .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        using var consumerEvidence = await database.ConsumerEvidenceAsync(context, request);
        var unavailable = await database.Writer(context, new EvidenceOwner(database, context, EvidenceFailure.StaleConsumerGeneration))
            .RequalifyActivationAsync(consumerEvidence, transaction.TransactionId, database.HostId, database.Scope,
                request.Proposed.PolicyId, request.Proposed.PublicationId, Now);
        await Assert.That(unavailable).IsNull();
        await transaction.RollbackAsync();
        await database.AssertCountsAsync(2, 1, 3);
    }

    [Test]
    public async Task Current_activation_deadline_comes_from_requalified_evidence_not_publication_history()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        request = await database.PrepareActiveAsync(request);
        await using (var writer = database.Context())
            await Assert.That((await database.Writer(writer, new EvidenceOwner(database, writer)).PublishAsync(request)).Outcome)
                .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        using var consumerEvidence = await database.ConsumerEvidenceAsync(context, request);
        var current = await database.Writer(context,
            new EvidenceOwner(database, context, approvalLifetime: TimeSpan.FromMinutes(5)))
            .RequalifyActivationAsync(consumerEvidence, transaction.TransactionId, database.HostId, database.Scope,
                request.Proposed.PolicyId, request.Proposed.PublicationId, Now);
        await Assert.That(current).IsEqualTo(Now.AddMinutes(5));
        await Assert.That((await context.ModerationPolicyPublications.SingleAsync(
            row => row.Id == request.Proposed.PublicationId)).EvidenceValidUntil)
            .IsEqualTo(Now.AddHours(1));
    }

    [Test]
    public async Task Shorter_measurement_deadline_bounds_publication_and_current_requalification()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        request = await database.PrepareActiveAsync(request);
        await using (var writer = database.Context())
        {
            var published = await database.Writer(writer,
                new EvidenceOwner(database, writer, measurementLifetime: TimeSpan.FromMinutes(5))).PublishAsync(request);
            await Assert.That(published.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
            await Assert.That(published.Publication!.EvidenceValidUntil).IsEqualTo(Now.AddMinutes(5));
        }
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        using var consumerEvidence = await database.ConsumerEvidenceAsync(context, request);
        var current = await database.Writer(context,
            new EvidenceOwner(database, context, measurementLifetime: TimeSpan.FromMinutes(1)))
            .RequalifyActivationAsync(consumerEvidence, transaction.TransactionId, database.HostId, database.Scope,
                request.Proposed.PolicyId, request.Proposed.PublicationId, Now);
        await Assert.That(current).IsEqualTo(Now.AddMinutes(1));
        await Assert.That((await context.ModerationPolicyPublications.SingleAsync(
            row => row.Id == request.Proposed.PublicationId)).EvidenceValidUntil)
            .IsEqualTo(Now.AddMinutes(5));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Missing_persisted_obligation_rows_cannot_be_certified_as_a_complete_subset(bool consumer)
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        request = await database.PrepareActiveAsync(request);
        await using (var writer = database.Context())
            await Assert.That((await database.Writer(writer, new EvidenceOwner(database, writer)).PublishAsync(request)).Outcome)
                .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using (var corrupt = database.Context())
        {
            corrupt.ModerationTrustDomainId = database.Scope.TrustDomainId;
            // Simulate damaged storage, not a supported mutation API. The compiler independently
            // knows the complete required set even if every row in one obligation group vanished.
            if (consumer)
                corrupt.RemoveRange(await corrupt.Set<ModerationPolicyPublicationConsumer>()
                    .Where(row => row.PublicationId == request.Proposed.PublicationId).ToListAsync());
            else
                corrupt.RemoveRange(await corrupt.Set<ModerationPolicyPublicationPrerequisite>()
                    .Where(row => row.PublicationId == request.Proposed.PublicationId).ToListAsync());
            await corrupt.SaveChangesAsync();
        }
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        using var consumerEvidence = await database.ConsumerEvidenceAsync(context, request);
        await Assert.That(await database.Writer(context, new EvidenceOwner(database, context))
            .RequalifyActivationAsync(consumerEvidence, transaction.TransactionId, database.HostId, database.Scope,
                request.Proposed.PolicyId, request.Proposed.PublicationId, Now)).IsNull();
    }

    [Test]
    public async Task Failure_after_graph_flush_leaves_no_publication_children_pointer_or_epoch_advance()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context(new RejectSavedGraph());
        await Assert.That(async () => await database.Writer(context).PublishAsync(database.Request()))
            .Throws<PublicationWriteInterrupted>();
        await database.AssertCountsAsync(0, 0, 1);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.Set<ModerationPolicyPublicationPlan>().CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.Set<ModerationPolicyPublicationReason>().CountAsync()).IsEqualTo(0);
    }
}
