using System.Text.Json;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    [Arguments(ModerationCaseTargetKind.EventVersion)]
    [Arguments(ModerationCaseTargetKind.ParticipantIncident)]
    [Arguments(ModerationCaseTargetKind.Actor)]
    [Arguments(ModerationCaseTargetKind.Service)]
    [Arguments(ModerationCaseTargetKind.Resource)]
    [Arguments(ModerationCaseTargetKind.Form)]
    [Arguments(ModerationCaseTargetKind.Destination)]
    [Arguments(ModerationCaseTargetKind.HiddenContentReceipt)]
    [Arguments(ModerationCaseTargetKind.LocalIncident)]
    public async Task TypedSubmissionReturnsOnlyOriginalCommittedAcceptanceAndReplaysAfterExpiry(ModerationCaseTargetKind kind)
    {
        await using var database = await NativeDatabase.CreateAsync(kind);
        var issued = await database.IssueAsync();
        var command = Submission(issued.Receipt.Id, issued.Token, database.Request().Material);
        var originalToken = command.PresentedToken.ToArray();
        await using var context = database.Context();
        var handler = new SubmitModerationAllegationCommandHandler(Committer(context, database));
        var accepted = await handler.ExecuteAsync(command);
        await Assert.That(accepted.IsSuccess).IsTrue();
        await Assert.That(accepted.AcceptedAt).IsEqualTo((DateTimeOffset?)database.Clock.Now);
        var serialized = JsonSerializer.SerializeToElement(accepted);
        await Assert.That(serialized.TryGetProperty("CaseId", out _)).IsFalse();
        await Assert.That(serialized.TryGetProperty("AcceptedCaseRevision", out _)).IsFalse();
        await Assert.That(serialized.TryGetProperty("PresentedToken", out _)).IsFalse();
        await Assert.That(serialized.TryGetProperty("CallerUserId", out _)).IsFalse();
        await Assert.That(serialized.TryGetProperty("Evidence", out _)).IsFalse();
        await Assert.That(serialized.TryGetProperty("Subject", out _)).IsFalse();
        database.Clock.Now = database.Clock.Now.AddHours(1);
        var replay = await handler.ExecuteAsync(command);
        await Assert.That(replay).IsEqualTo(accepted);
        await Assert.That(command.PresentedToken.ToArray()).IsEquivalentTo(originalToken);
        context.ModerationTrustDomainId = database.TrustId;
        var acknowledgement = await context.ModerationCaseIntakeAcknowledgements.SingleAsync();
        await Assert.That(accepted.Id).IsEqualTo(acknowledgement.Id);
        await Assert.That(accepted.AcceptedAt).IsEqualTo((DateTimeOffset?)acknowledgement.AcceptedAt);
        var allegation = await context.ModerationCases.SingleAsync();
        await Assert.That(allegation.Subject).IsNull();
        await Assert.That(allegation.State).IsEqualTo(ModerationCaseState.Open);
        await Assert.That((await context.ModerationCaseTargetReceipts.SingleAsync()).Revision).IsEqualTo(2L);
        await Assert.That(await context.ModerationCaseReadGrants.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.ModerationFindings.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.ModerationSanctions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments("receipt")]
    [Arguments("token")]
    [Arguments("narrative")]
    public async Task TypedSubmissionRejectsChangedAcceptedIntentWithoutLeakingCaseStatus(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var issued = await database.IssueAsync();
        var command = Submission(issued.Receipt.Id, issued.Token, database.Request().Material);
        await using var context = database.Context();
        var handler = new SubmitModerationAllegationCommandHandler(Committer(context, database));
        await Assert.That((await handler.ExecuteAsync(command)).IsSuccess).IsTrue();
        var differentToken = command.PresentedToken.ToArray();
        differentToken[0] ^= 1;
        var changed = defect switch
        {
            "receipt" => command with { ReceiptId = Guid.CreateVersion7() },
            "narrative" => command with { Narrative = new byte[] { 1, 2, 3 } },
            _ => command with { PresentedToken = differentToken }
        };
        var rejected = await handler.ExecuteAsync(changed);
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.AcceptedAt).IsNull();
        context.ModerationTrustDomainId = database.TrustId;
        await Assert.That(await context.ModerationCases.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(1);
        await Assert.That((await context.ModerationCaseTargetReceipts.SingleAsync()).Revision).IsEqualTo(2L);
    }

    [Test]
    public async Task TypedSubmissionInvalidIdentityDoesNotConsumeReceiptOrCreateAnAllegation()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var issued = await database.IssueAsync();
        var command = Submission(issued.Receipt.Id, issued.Token, database.Request().Material) with { OperationId = Guid.Empty };
        await using var context = database.Context();
        var rejected = await new SubmitModerationAllegationCommandHandler(Committer(context, database)).ExecuteAsync(command);
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.AcceptedAt).IsNull();
        context.ModerationTrustDomainId = database.TrustId;
        await Assert.That(await context.ModerationCases.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(0);
        await Assert.That((await context.ModerationCaseTargetReceipts.SingleAsync()).Revision).IsEqualTo(1L);
    }

    private static SubmitModerationAllegationCommand Submission(
        Guid receiptId, ReadOnlyMemory<byte> token, ReadOnlyMemory<byte> narrative) => new()
    {
        OperationId = Guid.CreateVersion7(),
        ReceiptId = receiptId, PresentedToken = token, Narrative = narrative
    };
}
