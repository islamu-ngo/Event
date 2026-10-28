using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Application.Services.Moderation;

/// <summary>
/// Authenticated owner operations never take their principal from a request body.
/// Maintenance escalation acquires notification policy locks before native identity/store fences.
/// </summary>
public sealed class ModerationRecognitionRecovery(
    IModerationRecognitionRecoveryRepository repository, IUserContext user,
    ISettingMutationLock settings, IModerationRecognitionRecoveryNotifications notifications)
{
    public Task<ModerationRecognitionOwnerContext?> ReadOwnerContextAsync(Guid escalationId, CancellationToken token) =>
        repository.ReadOwnerContextAsync(escalationId, user.GetRequiredUserId(), token);

    public Task<ModerationRecognitionDisposition> AssessAsync(
        ModerationRecognitionRecoveryAssessment assessment, CancellationToken token) =>
        repository.AssessAsync(assessment, user.GetRequiredUserId(), token);

    public Task<ModerationRecognitionDisposition> AuthorizeAsync(
        Guid dispositionId, long expectedRevision, CancellationToken token) =>
        repository.AuthorizeAsync(dispositionId, expectedRevision, user.GetRequiredUserId(), token);

    public Task<ModerationRecognitionEscalation?> ReadEscalationAsync(Guid id, CancellationToken token) =>
        repository.ReadEscalationAsync(id, user.GetRequiredUserId(), token);

    public Task<IReadOnlyList<ModerationRecognitionEscalation>> ReadEscalationsAsync(
        Guid? afterId, int limit, CancellationToken token) =>
        repository.ReadEscalationsAsync(user.GetRequiredUserId(), afterId, limit, token);

    public async Task<IReadOnlyList<ModerationRecognitionRecoveryRemedy>> ReadRemediesAsync(
        byte[] originalReceiptHash, CancellationToken token) =>
        (await repository.ReadRemediesAsync(originalReceiptHash, token)).Select(row =>
            new ModerationRecognitionRecoveryRemedy(row.Id, row.IntentId, row.SourceId, row.PendingRevision,
                row.Grounds, row.ProtectedAt, row.RemedyUntil, row.RetainUntil,
                row.OriginalMaterialReceipt, row.ObservedMaterialCommitment,
                row.GroundsReceipt, row.LegalMandateReceipt, row.CoverageReceipt)).ToArray();

    public Task AcknowledgeAsync(Guid id, CancellationToken token) =>
        repository.AcknowledgeAsync(id, user.GetRequiredUserId(), token);

    public Task EscalateAsync(Guid intentId, Guid? actionRecoveryId, CancellationToken token) =>
        settings.ExecuteOrderedGroupsAsync([[GovernanceSettingKeys.Email.DeliveryEnabled]], async ct =>
        {
            await repository.EscalateAsync(intentId, actionRecoveryId, notifications, ct);
            return true;
        }, token);
}

/// <summary>Protected original-receipt view; authentication hashes and provider material never leave the owner.</summary>
public sealed record ModerationRecognitionRecoveryRemedy(Guid Id, Guid IntentId, Guid SourceId,
    long PendingRevision, ModerationRecognitionDispositionGrounds Grounds, DateTimeOffset ProtectedAt,
    DateTimeOffset RemedyUntil, DateTimeOffset RetainUntil, string? OriginalMaterialReceipt,
    string ObservedMaterialCommitment, string GroundsReceipt, string LegalMandateReceipt, string CoverageReceipt);
