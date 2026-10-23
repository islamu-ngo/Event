using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Explore.Domain;

/// <summary>
/// Operational uncertainty about one original action candidate, not evidence of an
/// identity match or a sanction against the fresh subject. No identifier bytes are kept.
/// </summary>
public sealed class ModerationRecognitionActionRecovery
{
    private ModerationRecognitionActionRecovery() { }
    public Guid Id { get; private set; }
    public Guid FreshSubjectId { get; private set; }
    public Guid VerifiedBindingId { get; private set; }
    public Guid CaptureItemId { get; private set; }
    public string CaptureMaterialReceipt { get; private set; } = string.Empty;
    public Guid OriginalIntentId { get; private set; }
    public Guid PrivacyOwnerHumanId { get; private set; }
    public Guid RecoveryOwnerHumanId { get; private set; }
    public AuthenticationProviderKind ProviderKind { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset DispositionBy { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public static ModerationRecognitionActionRecovery Receive(Guid id, Guid freshSubjectId, Guid verifiedBindingId,
        ModerationRecognitionCaptureItem item, ModerationRecognitionPending receipt, DateTimeOffset now)
    {
        ModerationHostBinding.RequireUuidV7(id, nameof(id));
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(receipt);
        item.ValidatePersistedState();
        receipt.ValidatePersistedState();
        if (freshSubjectId == Guid.Empty || verifiedBindingId == Guid.Empty
            || item.Purpose != ModerationReferenceRecognition.SeparatelyQualifiedFinalAction
            || receipt.IntentId != item.IntentId || receipt.SubjectId != item.ErasureSubjectId
            || !receipt.HasCompleteMaterial || receipt.AuthoritySequence is null
            || receipt.State != ModerationRecognitionPendingState.AuthorityBound
            || receipt.Failure != ModerationRecognitionPendingFailure.None
            || !receipt.Material.Any(original => original.Id == item.Id)
            || !ModerationRecognitionContinuityBinding.WithinOriginalInterval(item, now))
            throw new InvalidOperationException("Deferred action recovery needs an exact settled candidate, not a matched identity.");
        return new()
        {
            Id = id,
            FreshSubjectId = freshSubjectId,
            VerifiedBindingId = verifiedBindingId,
            CaptureItemId = item.Id,
            CaptureMaterialReceipt = item.ComputeMaterialReceipt(),
            OriginalIntentId = item.IntentId,
            PrivacyOwnerHumanId = receipt.PrivacyOwnerHumanId,
            RecoveryOwnerHumanId = receipt.RecoveryOwnerHumanId,
            ProviderKind = item.ProviderKind,
            ReceivedAt = now,
            DispositionBy = item.EndsAt is { } end && end < item.RetainUntil ? end : item.RetainUntil
        };
    }

    public bool CouldAffect(ModerationRecognitionCaptureItem item, AuthorityScope target,
        ModerationCapability capability, DateTimeOffset now)
    {
        ValidatePersistedState();
        item.ValidatePersistedState();
        if (item.ComputeMaterialReceipt() != CaptureMaterialReceipt)
            throw new InvalidOperationException("Deferred recovery lost its original immutable candidate.");
        return ResolvedAt is null && now >= ReceivedAt && now < DispositionBy
            && item.Id == CaptureItemId && item.IntentId == OriginalIntentId
            && item.Purpose == ModerationReferenceRecognition.SeparatelyQualifiedFinalAction
            && (item.PurposeCapabilityMask & (1 << (int)capability)) != 0
            && item.Scope.Contains(target)
            && ModerationRecognitionContinuityBinding.WithinOriginalInterval(item, now);
    }

    /// <summary>The native owner calls only after complete actual matching or lawful disposition.</summary>
    public void Resolve(DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero || now < ReceivedAt || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Recovery resolution requires finite original UTC ordering.");
        ResolvedAt ??= now;
    }

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(Id, nameof(Id));
        if (FreshSubjectId == Guid.Empty || VerifiedBindingId == Guid.Empty
            || CaptureItemId == Guid.Empty || OriginalIntentId == Guid.Empty
            || CaptureMaterialReceipt.Length != 64 || !CaptureMaterialReceipt.All(char.IsAsciiHexDigitLower)
            || PrivacyOwnerHumanId == Guid.Empty || RecoveryOwnerHumanId == Guid.Empty
            || PrivacyOwnerHumanId == RecoveryOwnerHumanId || !Enum.IsDefined(ProviderKind)
            || ReceivedAt.Offset != TimeSpan.Zero || ReceivedAt == DateTimeOffset.MinValue
            || DispositionBy.Offset != TimeSpan.Zero || DispositionBy <= ReceivedAt
            || DispositionBy == DateTimeOffset.MaxValue
            || ResolvedAt is { } resolved && (resolved.Offset != TimeSpan.Zero || resolved < ReceivedAt
                || resolved == DateTimeOffset.MaxValue))
            throw new InvalidOperationException("Stored deferred recovery must retain exact owners and finite original limits.");
    }
}
