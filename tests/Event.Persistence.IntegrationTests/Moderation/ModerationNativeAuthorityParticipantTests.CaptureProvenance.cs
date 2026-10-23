using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Domain;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNativeAuthorityParticipantTests
{
    [Test]
    [Arguments("current")]
    [Arguments("copied-grant")]
    [Arguments("foreign-context")]
    [Arguments("previous-session")]
    [Arguments("rebound-previous-session")]
    [Arguments("wrong-effect")]
    [Arguments("replaced-acquisition")]
    [Arguments("expired-transaction")]
    public async Task Notice_capture_borrows_only_the_actual_current_parent(string substitution)
    {
        await using var database = await NativeDatabase.CreateAsync();
        await using var context = database.Context();
        await using var foreign = database.Context();
        var clock = new FixedClock();
        var privacy = database.Privacy.Authority(context, clock);
        var participant = database.Participant(context, database.Subject.Id, clock: clock);
        var operation = database.Transition(ModerationOperationKind.RecordResponse);
        var store = new ModerationStore(context, participant, clock, privacy,
            ModerationLedgerPersistenceTests.Capture(context, privacy, participant, clock));
        IModerationSession? previous = null;
        if (substitution is "previous-session" or "rebound-previous-session")
            await store.ExecuteAsync(operation, (session, _) =>
            {
                previous = session;
                return Task.FromResult(ModerationOperationResult.Unavailable());
            }, default);

        IModerationSession? originalSession = null;
        ModerationAuthorityGrant? originalGrant = null;
        ModerationEffectIntent? originalEffect = null;
        await store.ExecuteAsync(operation, async (session, token) =>
        {
            var grant = (await participant.ResolveAsync(session, operation, token)).Grant!;
            if (substitution == "rebound-previous-session")
            {
                var replacement = await participant.AcquireAsync(previous!, operation, token);
                await Assert.That(replacement.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
                grant = replacement.Grant!;
            }
            var decision = (await session.FindDecisionAsync(operation.DecisionId,
                operation.Subject, operation.Target, token))!;
            decision.RecordResponse(1, Now);
            var effect = ModerationEffectIntent.Create(operation.EffectId, operation.OperationId,
                decision.Id, decision.Revision, null, null, operation.Target.TrustDomainId,
                (int)operation.Target.Kind, operation.Target.Id, (int)operation.Subject.Kind,
                operation.Subject.Id, (int)operation.Kind, grant.PolicyRevision,
                grant.QualificationRevision, Now, null, null);
            context.ModerationEffectIntents.Add(effect);
            originalSession = session;
            originalGrant = grant;
            originalEffect = effect;
            if (substitution == "expired-transaction") return ModerationOperationResult.Unavailable();
            if (substitution == "replaced-acquisition")
                await participant.AcquireAsync(session, operation, token);
            var requestedEffect = substitution == "wrong-effect"
                ? ModerationEffectIntent.Create(Guid.CreateVersion7(), operation.OperationId,
                    decision.Id, decision.Revision, null, null, operation.Target.TrustDomainId,
                    (int)operation.Target.Kind, operation.Target.Id, (int)operation.Subject.Kind,
                    operation.Subject.Id, (int)operation.Kind, grant.PolicyRevision,
                    grant.QualificationRevision, Now, null, null)
                : effect;
            var borrowed = await participant.BorrowNoticeSourceAuthorityAsync(
                substitution == "foreign-context" ? foreign : context,
                substitution is "previous-session" or "rebound-previous-session" ? previous! : session,
                requestedEffect, substitution == "copied-grant" ? grant with { } : grant, token);
            await Assert.That(borrowed is not null).IsEqualTo(substitution == "current");
            return ModerationOperationResult.Unavailable();
        }, default);
        if (substitution == "expired-transaction")
            await Assert.That(await participant.BorrowNoticeSourceAuthorityAsync(
                context, originalSession!, originalEffect!, originalGrant!, default)).IsNull();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var inspect = database.Context();
        await Assert.That(await inspect.ModerationEffectIntents.AnyAsync()).IsFalse();
        await Assert.That(await inspect.ModerationNoticeSources.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task Native_inert_capture_does_not_require_substantive_notice_sources()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var operation = ModerationOperation.Propose(database.Subject, database.Scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1,
            new(database.Case.Id, database.Rule.Rule, [ModerationCapability.Publish],
                Now, Now.AddHours(1), null, 1));
        var result = await database.ExecuteAsync(database.IssuerId, operation);
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await using var inspect = database.Context();
        inspect.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var source = await inspect.ModerationNoticeSources.SingleAsync(row => row.EffectId == operation.EffectId);
        await Assert.That(JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!
            .HasNoSubstantiveWork).IsTrue();
        await Assert.That(await inspect.ModerationNotices.AnyAsync()).IsFalse();
    }
}
