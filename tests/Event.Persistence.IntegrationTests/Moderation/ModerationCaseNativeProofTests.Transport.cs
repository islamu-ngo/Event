using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
    public async Task NativeCommandsIssueCommittedBearerAndPreserveOnlyOriginalAllegationAcceptance(
        ModerationCaseTargetKind kind)
    {
        await using var database = await NativeDatabase.CreateAsync(kind);
        await using var context = database.Context();
        var intake = IssuanceCommitter(context, database, database.Caller);
        var request = database.Request();
        var receipt = await new IssueModerationSubmissionReceiptCommandHandler(intake).ExecuteAsync(new()
        {
            Kind = request.Kind, NativeKind = request.NativeKind, EventId = request.EventId,
            NativeTargetId = request.NativeTargetId, RelationshipKind = request.RelationshipKind,
            RelationshipSourceId = request.RelationshipSourceId,
            OccurrenceMaterial = request.OccurrenceMaterial, Material = request.Material
        });
        await Assert.That(receipt.IsSuccess).IsTrue();
        await Assert.That(receipt.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.PresentedToken.Length).IsEqualTo(32);
        await Assert.That(receipt.ExpiresAt).IsNotNull();
        await using (var persisted = database.Context())
        {
            var retained = await persisted.ModerationCaseTargetReceipts.SingleAsync(row => row.Id == receipt.Id);
            await Assert.That(receipt.ExpiresAt).IsEqualTo(retained.ExpiresAt);
            await Assert.That(retained.ConsumedAt).IsNull();
            await Assert.That(await persisted.ModerationCases.AnyAsync()).IsFalse();
        }
        var expectedToken = receipt.PresentedToken.ToArray();
        if (MemoryMarshal.TryGetArray(receipt.PresentedToken, out var exposed))
            CryptographicOperations.ZeroMemory(exposed.Array!);
        await Assert.That(receipt.PresentedToken.ToArray()).IsEquivalentTo(expectedToken);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var receiptBody = JsonDocument.Parse(JsonSerializer.Serialize(receipt, options));
        await Assert.That(receiptBody.RootElement.GetProperty("presentedToken").GetBytesFromBase64())
            .IsEquivalentTo(expectedToken);
        foreach (var confidential in new[] { "target", "evidence", "callerUserId", "tenantId", "relationshipSourceId", "caseId" })
            await Assert.That(receiptBody.RootElement.TryGetProperty(confidential, out _)).IsFalse();

        var command = new SubmitModerationAllegationCommand
        {
            OperationId = Guid.CreateVersion7(),
            ReceiptId = receipt.Id, PresentedToken = expectedToken, Narrative = request.Material
        };
        var submit = new SubmitModerationAllegationCommandHandler(intake);
        var acceptance = await submit.ExecuteAsync(command);
        await Assert.That(acceptance.IsSuccess).IsTrue();
        await Assert.That(acceptance.Id).IsNotEqualTo(receipt.Id);
        database.Clock.Now = database.Clock.Now.AddHours(1);
        var replay = await submit.ExecuteAsync(command);
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(acceptance.Id);
        await Assert.That(replay.AcceptedAt).IsEqualTo(acceptance.AcceptedAt);
        var conflict = await submit.ExecuteAsync(command with { ReceiptId = Guid.CreateVersion7() });
        await Assert.That(conflict.IsSuccess).IsFalse();
        await Assert.That(conflict.FailureCode).IsEqualTo(Explore.Application.Responses.FailureCodes.ConcurrencyConflict);
        using var acceptanceBody = JsonDocument.Parse(JsonSerializer.Serialize(acceptance, options));
        foreach (var confidential in new[]
                 { "presentedToken", "target", "evidence", "callerUserId", "tenantId", "status", "caseId", "acceptedCaseRevision" })
            await Assert.That(acceptanceBody.RootElement.TryGetProperty(confidential, out _)).IsFalse();
    }
}
