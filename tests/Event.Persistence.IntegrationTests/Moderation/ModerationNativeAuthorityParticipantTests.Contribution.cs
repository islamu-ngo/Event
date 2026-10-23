using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNativeAuthorityParticipantTests
{
    [Test]
    [Arguments(0, false)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(3, false)]
    [Arguments(4, false)]
    [Arguments(5, false)]
    [Arguments(6, false)]
    [Arguments(7, false)]
    [Arguments(8, false)]
    [Arguments(9, false)]
    [Arguments(10, false)]
    [Arguments(11, false)]
    [Arguments(0, true)]
    [Arguments(12, true)]
    [Arguments(13, false)]
    public async Task NativeContributionDraftRead_RequiresExactReceiptDisclosureAndCurrentAuthor(int substitution, bool terminalContribution)
    {
        await using var database = await NativeDatabase.CreateAsync();
        ModerationSanction? originalSanction = null;
        if (terminalContribution)
        {
            // Historical committed contribution is not measured activation evidence.
            originalSanction = await database.ApproveAsync();
            await using var writer = database.Context();
            writer.Add(originalSanction);
            await writer.SaveChangesAsync();
        }
        var operation = terminalContribution
            ? database.Transition(ModerationOperationKind.Revoke, originalSanction!.Revision, originalSanction.Id)
            : database.Transition(ModerationOperationKind.RecordResponse);
        var originalDecisionRevision = database.Decision.Revision;
        var noticeDecisionRevision = terminalContribution && substitution != 12
            || substitution == 13 ? originalDecisionRevision : originalDecisionRevision + 1;
        var target = new ModerationPolicyTarget(database.Scope, database.Subject.Kind, database.Subject.Id,
            Guid.CreateVersion7(), 1, "resource.read", null);
        var contribution = new ModerationPolicyContribution(database.Decision.Id, database.Decision.Rule,
            database.Scope, database.Subject.Kind, database.Subject.Id, database.Decision.StartsAt, null,
            database.Rule.Effects.Select(effect => new ModerationPolicyEffectTarget(effect, target)).ToImmutableArray(),
            [new(null, target)]);
        var selectionId = Guid.CreateVersion7();
        var facts = ImmutableArray.Create(new ModerationNoticeSafeText("Explicitly approved contribution disclosure."));
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var selection = new ModerationContributionSelection
            {
                Id = selectionId, EffectId = operation.EffectId, DecisionId = database.Decision.Id,
                DecisionRevision = substitution == 5 ? noticeDecisionRevision + 1 : noticeDecisionRevision,
                CaseId = database.Case.Id, CaseRevision = 1,
                PolicyId = database.PublicationRequest.Proposed.PolicyId, PolicyRevision = 1,
                PublicationId = database.PublicationRequest.Proposed.PublicationId, ProfileId = database.Rule.Id,
                HostBindingId = database.HostId, TrustDomainId = database.Scope.TrustDomainId,
                TenantId = database.TenantId, AuthoredByHumanId = database.UrgencyAssessorId,
                WriterGeneration = substitution == 11 ? 5 : 3, Revision = 1,
                SelectionDigest = ModerationNoticeSnapshotFactory.SelectionDigest(contribution),
                StartsAt = contribution.StartsAt, EventWindowEnd = null, RecordedAt = Now, ValidUntil = Now.AddMinutes(5),
                Bindings = ModerationContributionSelectionProjection.Bindings(selectionId,
                    substitution == 6 ? contribution with
                    {
                        Consequences = [new(null, target with { TargetRevision = 2 })]
                    } : contribution)
            };
            writer.ModerationContributionSelections.Add(selection);
            var selectionSource = new RecordModerationContributionSelectionSource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope,
                ExpectedQualificationRevision = selection.WriterGeneration - 1,
                SelectionId = selection.Id, EffectId = operation.EffectId, CaseId = selection.CaseId,
                ExpectedCaseRevision = selection.CaseRevision, DecisionId = selection.DecisionId,
                ExpectedDecisionRevision = selection.DecisionRevision, PolicyId = selection.PolicyId,
                ExpectedPolicyRevision = 1, PublicationId = selection.PublicationId, ProfileId = selection.ProfileId,
                Contribution = contribution, ValidUntil = selection.ValidUntil
            };
            if (substitution != 1)
                writer.ModerationQualificationMutationReceipts.Add(ModerationQualificationMutationReceipt.Complete(
                    selectionSource.OperationId, substitution == 2 ? new string('b', 64) : Digest(selectionSource),
                    database.HostId, database.Scope.TrustDomainId, database.TenantId,
                    ModerationQualificationSourceKind.ContributionSelection, selectionId,
                    substitution == 3 ? database.IssuerId : database.UrgencyAssessorId, 1, selection.WriterGeneration, Now));
            var intent = new ModerationNoticeDisclosureIntent(
                Guid.CreateVersion7(), operation.EffectId, database.Decision.Id, noticeDecisionRevision, database.Case.Id, 1,
                selection.PolicyId, 1, selection.PublicationId, selection.ProfileId, database.Decision.Id,
                substitution == 7 ? new string('a', 64) : selection.SelectionDigest,
                new(database.PublicationRequest.Proposed.Classes.Single().Meaning),
                new(database.PublicationRequest.Proposed.Reasons.Single().Meaning), facts,
                new("Response remains available."), new("Request independent review."),
                new("No automated finding."), new("Purpose-bound retention."), Now.AddMinutes(10), Now.AddYears(1));
            var approval = ModerationNoticeDisclosureApproval.Approve(
                intent, database.HostId, database.Scope.TrustDomainId, database.TenantId, database.AssessorId, 4, Now);
            var disclosureSource = new ApproveModerationNoticeDisclosureSource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 3, Intent = intent
            };
            writer.ModerationNoticeDisclosureApprovals.Add(approval);
            writer.ModerationQualificationMutationReceipts.Add(ModerationQualificationMutationReceipt.Complete(
                disclosureSource.OperationId, Digest(disclosureSource), database.HostId,
                database.Scope.TrustDomainId, database.TenantId, ModerationQualificationSourceKind.NoticeDisclosure,
                approval.Id, database.AssessorId, 1, 4, Now));
            if (substitution == 4)
                writer.PlatformUserRoles.Remove(await writer.PlatformUserRoles.SingleAsync(
                    row => row.UserId == database.UrgencyAssessorId));
            var epoch = await writer.ModerationQualificationEpochs.SingleAsync();
            epoch.Advance(2);
            epoch.Advance(3);
            await writer.SaveChangesAsync();
        }

        await using var context = database.Context();
        var clock = new FixedClock();
        var privacyAuthority = database.Privacy.Authority(context, clock);
        var participant = database.Participant(context,
            terminalContribution ? database.IssuerId : database.Subject.Id, clock: clock);
        var store = new ModerationStore(context, participant, clock, privacyAuthority, ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock));
        ModerationNoticeNativeContributionDraft? observed = null;
        await store.ExecuteAsync(operation, async (session, token) =>
        {
            var resolution = await participant.ResolveAsync(session, operation, token);
            await Assert.That(resolution.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
            var decision = (await session.FindDecisionAsync(operation.DecisionId, operation.Subject, operation.Target, token))!;
            ModerationSanction? sanction = null;
            if (terminalContribution)
            {
                sanction = (await session.FindSanctionAsync(
                    originalSanction!.Id, operation.Subject, operation.Target, token))!;
                sanction.Revoke(originalSanction.Revision, Now);
                if (substitution == 12)
                    context.Entry(decision).Property(row => row.Revision).CurrentValue = decision.Revision + 1;
            }
            else if (substitution != 13)
                decision.RecordResponse(1, Now);
            var effect = ModerationEffectIntent.Create(operation.EffectId, operation.OperationId, decision.Id, decision.Revision,
                sanction?.Id, sanction?.Revision, database.Scope.TrustDomainId, (int)database.Scope.Kind, database.Scope.Id,
                (int)database.Subject.Kind, database.Subject.Id, (int)operation.Kind, 1,
                resolution.Grant!.QualificationRevision, Now, null, null);
            context.ModerationEffectIntents.Add(effect);
            var publication = await context.ModerationPolicyPublications.AsNoTracking().SingleAsync(token);
            if (substitution == 10) clock.UtcNow = Now.AddMinutes(5);
            observed = await participant.AcquireNoticeContributionDraftAsync(
                substitution == 9 ? Guid.CreateVersion7() : context.Database.CurrentTransaction!.TransactionId,
                publication, effect, decision, substitution == 8 ? resolution.Grant with { } : resolution.Grant,
                substitution == 10 ? Now.AddMinutes(5) : Now, token);
            return ModerationOperationResult.Unavailable();
        }, CancellationToken.None);
        if (substitution == 0)
        {
            await Assert.That(observed!.SelectionId).IsEqualTo(selectionId);
            await Assert.That(observed.WriterGeneration).IsEqualTo(3L);
            await Assert.That(observed.ValidUntil).IsEqualTo(Now.AddMinutes(5));
            await Assert.That(ModerationNoticeSnapshotFactory.SelectionDigest(observed.Contribution))
                .IsEqualTo(ModerationNoticeSnapshotFactory.SelectionDigest(contribution));
            await Assert.That(observed.Notice.Content.Facts.Single().Value).IsEqualTo(facts.Single().Value);
        }
        else
            await Assert.That(observed).IsNull();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That((await reader.ModerationDecisions.SingleAsync()).Revision).IsEqualTo(originalDecisionRevision);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
        if (terminalContribution)
        {
            var retainedSanction = await reader.ModerationSanctions.SingleAsync();
            await Assert.That(retainedSanction.State).IsEqualTo(ModerationSanctionState.Active);
            await Assert.That(retainedSanction.Revision).IsEqualTo(originalSanction!.Revision);
        }
    }

    private static string Digest(ModerationQualificationMutation source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            source.GetType().FullName + "\n" + JsonSerializer.Serialize(source, source.GetType()))));
}
