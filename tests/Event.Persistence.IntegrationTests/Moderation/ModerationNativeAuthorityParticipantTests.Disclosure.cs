using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
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
    public async Task NativeDisclosureRead_RequiresExactAcceptanceAndCurrentFencedApprover(int substitution, bool terminalContribution)
    {
        await using var database = await NativeDatabase.CreateAsync();
        ModerationSanction? originalSanction = null;
        if (terminalContribution)
        {
            // Explicit historical starting-state factory, not measured activation evidence.
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
            ? originalDecisionRevision : originalDecisionRevision + 1;
        var facts = ImmutableArray.Create(new ModerationNoticeSafeText("Explicitly approved disclosure."));
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var intent = new ModerationNoticeDisclosureIntent(
                Guid.CreateVersion7(), operation.EffectId, database.Decision.Id, noticeDecisionRevision, database.Case.Id, 1,
                database.PublicationRequest.Proposed.PolicyId, 1, database.PublicationRequest.Proposed.PublicationId,
                database.Rule.Id, database.Decision.Id, new string('a', 64),
                new(database.PublicationRequest.Proposed.Classes.Single().Meaning),
                new(database.PublicationRequest.Proposed.Reasons.Single().Meaning),
                facts,
                new("Response remains available."), new("Request independent review."),
                new("No automated finding."), new("Purpose-bound retention."), Now.AddMinutes(10), Now.AddYears(1));
            var approval = ModerationNoticeDisclosureApproval.Approve(
                intent, database.HostId, database.Scope.TrustDomainId, database.TenantId, database.AssessorId, 3, Now);
            var source = new ApproveModerationNoticeDisclosureSource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 2, Intent = intent
            };
            // Native writer digest includes its closed mutation type, not just the JSON payload.
            var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                source.GetType().FullName + "\n" + System.Text.Json.JsonSerializer.Serialize(source))));
            writer.ModerationNoticeDisclosureApprovals.Add(approval);
            if (substitution != 1)
                writer.ModerationQualificationMutationReceipts.Add(ModerationQualificationMutationReceipt.Complete(
                    source.OperationId, substitution == 2 ? new string('b', 64) : digest, database.HostId,
                    database.Scope.TrustDomainId, database.TenantId, ModerationQualificationSourceKind.NoticeDisclosure,
                    approval.Id, substitution == 3 ? database.IssuerId : database.AssessorId,
                    1, 3, Now));
            if (substitution == 4)
                writer.PlatformUserRoles.Remove(await writer.PlatformUserRoles.SingleAsync(row => row.UserId == database.AssessorId));
            if (substitution == 5)
                approval.Revoke(1, database.AssessorId, Now);
            var epoch = await writer.ModerationQualificationEpochs.SingleAsync();
            epoch.Advance(2);
            await writer.SaveChangesAsync();
        }
        if (substitution == 8)
            database.NativeSources.CurrentMaximumReviewerLifetime = TimeSpan.FromMinutes(30);

        await using var context = database.Context();
        var clock = new FixedClock();
        var privacyAuthority = database.Privacy.Authority(context, clock);
        var participant = database.Participant(context,
            terminalContribution ? database.IssuerId : database.Subject.Id, clock: clock);
        var store = new ModerationStore(context, participant, clock, privacyAuthority, ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock));
        ModerationNoticeContentApproval? observed = null;
        await store.ExecuteAsync(operation, async (session, token) =>
        {
            var resolution = await participant.ResolveAsync(session, operation, token);
            await Assert.That(resolution.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
            var decision = (await session.FindDecisionAsync(
                operation.DecisionId, operation.Subject, operation.Target, token))!;
            ModerationSanction? sanction = null;
            if (terminalContribution)
            {
                sanction = (await session.FindSanctionAsync(
                    originalSanction!.Id, operation.Subject, operation.Target, token))!;
                sanction.Revoke(originalSanction.Revision, Now);
                if (substitution == 12)
                    context.Entry(decision).Property(row => row.Revision).CurrentValue = decision.Revision + 1;
            }
            else
                decision.RecordResponse(1, Now);
            var effect = ModerationEffectIntent.Create(operation.EffectId, operation.OperationId, decision.Id, decision.Revision,
                sanction?.Id, sanction?.Revision, database.Scope.TrustDomainId, (int)database.Scope.Kind, database.Scope.Id,
                (int)database.Subject.Kind, database.Subject.Id, (int)operation.Kind, 1,
                resolution.Grant!.QualificationRevision, Now, null, null);
            context.ModerationEffectIntents.Add(effect);
            var publication = await context.ModerationPolicyPublications.AsNoTracking().SingleAsync(token);
            if (substitution is 9 or 10 or 11)
            {
                var substituted = JsonSerializer.SerializeToNode(publication)!.AsObject();
                if (substitution == 9) substituted[nameof(publication.WriterGeneration)] = "substituted-native-source";
                else if (substitution == 10) substituted[nameof(publication.TrustDomainId)] = Guid.CreateVersion7();
                else substituted[nameof(publication.CatalogueRevision)] = publication.CatalogueRevision + 1;
                publication = substituted.Deserialize<ModerationPolicyPublication>()!;
            }
            var qualification = await participant.AcquireNoticeQualificationAsync(
                substitution == 7 ? Guid.CreateVersion7() : context.Database.CurrentTransaction!.TransactionId,
                publication, effect, decision,
                substitution == 6 ? resolution.Grant with { } : resolution.Grant, Now, token);
            observed = qualification?.Content;
            return ModerationOperationResult.Unavailable();
        }, CancellationToken.None);
        if (substitution == 0)
            await Assert.That(observed!.Facts.Single().Value).IsEqualTo(facts.Single().Value);
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
}
