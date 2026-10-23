using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IModerationRecognitionRecoveryRepository
{
    /// <summary>Qualifies the exact recipient and current source and reads current state under the native transaction.</summary>
    Task<ModerationRecognitionOwnerContext?> ReadOwnerContextAsync(Guid escalationId,
        Guid authenticatedHuman, CancellationToken token);
    Task<ModerationRecognitionDisposition> AssessAsync(ModerationRecognitionRecoveryAssessment assessment,
        Guid authenticatedHuman, CancellationToken token);
    Task<ModerationRecognitionDisposition> AuthorizeAsync(Guid dispositionId, long expectedRevision,
        Guid authenticatedHuman, CancellationToken token);
    /// <summary>Called only inside the original retained identity and native serializable fences.</summary>
    Task<bool> HasLawfulDispositionAsync(PrivacyErasureRequest request, CancellationToken token);
    Task<bool> HasConsumedLawfulDispositionAsync(PrivacyErasureRequest request, CancellationToken token);
    Task<bool> HasRecoverableLawfulDispositionAsync(PrivacyErasureRequest request, CancellationToken token);
    /// <summary>Checks the exact retained fact independently; never manufactures capture settlement.</summary>
    Task BindLawfulDispositionAsync(PrivacyErasureIntent fact, CancellationToken token);
    /// <summary>Separate replay verification is keyless and never means matching completed.</summary>
    Task<bool> VerifyLawfulDispositionAsync(PrivacyErasureIntent fact, CancellationToken token);
    Task EscalateAsync(Guid intentId, Guid? actionRecoveryId,
        IModerationRecognitionRecoveryNotifications notifications, CancellationToken token);
    Task<ModerationRecognitionEscalation?> ReadEscalationAsync(Guid id, Guid authenticatedHuman, CancellationToken token);
    Task<IReadOnlyList<ModerationRecognitionEscalation>> ReadEscalationsAsync(Guid authenticatedHuman,
        Guid? afterId, int limit, CancellationToken token);
    Task AcknowledgeAsync(Guid id, Guid authenticatedHuman, CancellationToken token);
    Task<IReadOnlyList<ModerationRecognitionDispositionRemedy>> ReadRemediesAsync(byte[] originalReceiptHash, CancellationToken token);
}

public sealed record ModerationRecognitionRecoveryAssessment(
    Guid Id, Guid IntentId, long ExpectedPendingRevision, Guid SourceId, long ExpectedSourceRevision,
    long ExpectedSourceGeneration, Guid AdoptionId, long ExpectedAdoptionRevision, long ExpectedAdoptionGeneration,
    ModerationRecognitionDispositionGrounds Grounds, DateTimeOffset ValidUntil);
