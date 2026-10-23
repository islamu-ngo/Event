using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Services;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNativeAuthorityParticipantTests
{
    [Test]
    public async Task Typed_native_proposal_replays_its_exact_receipt_without_duplicate_state_or_effect()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var command = NativeProposal(database);
        await using (var context = database.Context())
        {
            var clock = new FixedClock();
            var privacyAuthority = database.Privacy.Authority(context, clock);
            var participant = database.Participant(context, database.IssuerId, clock: clock);
            var operations = new ModerationOperationService(
                new ModerationStore(context, participant, clock, privacyAuthority, ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock)),
                participant, new ModerationEffectPlanner(), clock);
            var handler = new ProposeModerationDecisionCommandHandler(operations);
            var first = await handler.ExecuteAsync(command);
            var replay = await handler.ExecuteAsync(command);
            await Assert.That(first.IsSuccess).IsTrue();
            await Assert.That(replay).IsEqualTo(first);
            await Assert.That(first.Id).IsEqualTo(command.DecisionId);
            await Assert.That(first.Revision).IsEqualTo((long?)1);
            var changed = command with
            {
                Proposal = new ModerationProposal(database.Case.Id, database.Rule.Rule,
                    [ModerationCapability.Publish], Now, Now.AddMinutes(30), null, 1)
            };
            await Assert.That((await handler.ExecuteAsync(changed)).IsSuccess).IsFalse();
        }
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationDecisions.CountAsync(row => row.Id == command.DecisionId)).IsEqualTo(1);
        await Assert.That(await reader.ModerationOperationReceipts.CountAsync(row => row.Id == command.OperationId)).IsEqualTo(1);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync(row => row.Id == command.EffectId)).IsEqualTo(1);
        await Assert.That(await reader.OutboxMessages.CountAsync(row => row.Id == command.EffectId)).IsEqualTo(1);
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Typed_native_proposal_cannot_replay_after_its_actual_administrator_role_disappears()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var command = NativeProposal(database);
        await using (var context = database.Context())
        {
            var clock = new FixedClock();
            var privacyAuthority = database.Privacy.Authority(context, clock);
            var participant = database.Participant(context, database.IssuerId, clock: clock);
            var operations = new ModerationOperationService(
                new ModerationStore(context, participant, clock, privacyAuthority, ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock)),
                participant, new ModerationEffectPlanner(), clock);
            await Assert.That((await new ProposeModerationDecisionCommandHandler(operations).ExecuteAsync(command)).IsSuccess)
                .IsTrue();
        }
        await using (var writer = database.Context())
        {
            writer.PlatformUserRoles.RemoveRange(await writer.PlatformUserRoles
                .Where(row => row.UserId == database.IssuerId).ToListAsync());
            await writer.SaveChangesAsync();
        }
        await using (var context = database.Context())
        {
            var clock = new FixedClock();
            var privacyAuthority = database.Privacy.Authority(context, clock);
            var participant = database.Participant(context, database.IssuerId, clock: clock);
            var operations = new ModerationOperationService(
                new ModerationStore(context, participant, clock, privacyAuthority, ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock)),
                participant, new ModerationEffectPlanner(), clock);
            await Assert.That((await new ProposeModerationDecisionCommandHandler(operations).ExecuteAsync(command)).IsSuccess)
                .IsFalse();
        }
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationOperationReceipts.CountAsync(row => row.Id == command.OperationId)).IsEqualTo(1);
        await Assert.That(await reader.ModerationDecisions.CountAsync(row => row.Id == command.DecisionId)).IsEqualTo(1);
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Typed_native_proposal_validation_rejects_bad_identity_before_any_authority_write()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var command = NativeProposal(database) with { OperationId = Guid.Empty };
        await using (var context = database.Context())
        {
            var clock = new FixedClock();
            var privacyAuthority = database.Privacy.Authority(context, clock);
            var participant = database.Participant(context, database.IssuerId, clock: clock);
            var operations = new ModerationOperationService(
                new ModerationStore(context, participant, clock, privacyAuthority, ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock)),
                participant, new ModerationEffectPlanner(), clock);
            await Assert.That((await new ProposeModerationDecisionCommandHandler(operations).ExecuteAsync(command)).IsSuccess)
                .IsFalse();
        }
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationDecisions.AnyAsync(row => row.Id == command.DecisionId)).IsFalse();
        await Assert.That(await reader.ModerationOperationReceipts.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationNoticeSources.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.OutboxMessages.CountAsync()).IsEqualTo(0);
    }

    private static ProposeModerationDecisionCommand NativeProposal(NativeDatabase database) => new()
    {
        OperationId = Guid.CreateVersion7(), EffectId = Guid.CreateVersion7(), DecisionId = Guid.CreateVersion7(),
        Subject = database.Subject, Target = database.Scope, ExpectedRevision = 1, ExpectedPolicyRevision = 1,
        Proposal = new ModerationProposal(database.Case.Id, database.Rule.Rule, [ModerationCapability.Publish],
            Now, Now.AddHours(1), null, 1)
    };
}
