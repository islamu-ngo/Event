using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Explore.Domain;

/// <summary>
/// A fresh subject's reference to one original action contribution, not a sanction, identity
/// merger or enforcement grant. The capture item remains the sole original scope/source receipt.
/// Native readers must requalify that item, finding, sanction and adopted source on every use.
/// </summary>
public sealed class ModerationRecognitionContinuityBinding
{
    private ModerationRecognitionContinuityBinding() { }
    public Guid Id { get; private set; }
    public Guid FreshSubjectId { get; private set; }
    public Guid CaptureItemId { get; private set; }
    public string CaptureMaterialReceipt { get; private set; } = string.Empty;
    public Guid SanctionId { get; private set; }
    public Guid FindingId { get; private set; }
    public DateTimeOffset BoundAt { get; private set; }
    public DateTimeOffset RetainUntil { get; private set; }

    public static ModerationRecognitionContinuityBinding Bind(Guid id, Guid freshSubjectId,
        ModerationRecognitionCaptureItem item, ModerationDecision decision, ModerationSanction sanction,
        DateTimeOffset now)
    {
        item.ValidatePersistedState();
        decision.ValidatePersistedState();
        sanction.ValidatePersistedState();
        if (freshSubjectId == Guid.Empty || freshSubjectId == item.SubjectId
            || item.Purpose != ModerationReferenceRecognition.SeparatelyQualifiedFinalAction
            || !item.MatchesCurrent(decision, sanction, now) || !WithinOriginalInterval(item, now))
            throw new ArgumentException("Continuity requires a distinct fresh subject and current original action contribution.");
        var binding = new ModerationRecognitionContinuityBinding
        {
            Id = id,
            FreshSubjectId = freshSubjectId,
            CaptureItemId = item.Id,
            CaptureMaterialReceipt = item.ComputeMaterialReceipt(),
            SanctionId = item.SanctionId,
            FindingId = decision.FindingId!.Value,
            BoundAt = now,
            RetainUntil = item.RetainUntil
        };
        binding.ValidatePersistedState();
        return binding;
    }

    public bool IsCurrent(ModerationRecognitionCaptureItem item, ModerationDecision decision,
        ModerationSanction sanction, DateTimeOffset now) =>
        item.Id == CaptureItemId && item.ComputeMaterialReceipt() == CaptureMaterialReceipt
        && item.SanctionId == SanctionId && decision.FindingId == FindingId
        && item.SubjectId != FreshSubjectId && item.Purpose == ModerationReferenceRecognition.SeparatelyQualifiedFinalAction
        && item.RetainUntil == RetainUntil && now >= BoundAt && WithinOriginalInterval(item, now)
        && item.MatchesCurrent(decision, sanction, now);

    public static bool CoversAdmission(ModerationRecognitionCaptureItem item, AuthorityScope jurisdiction,
        DateTimeOffset now) =>
        item.Purpose == ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment
        && WithinOriginalInterval(item, now) && item.Scope.Contains(jurisdiction)
        && (item.PurposeCapabilityMask & ((1 << (int)ModerationCapability.Enrollment)
            | (1 << (int)ModerationCapability.Admission) | (1 << (int)ModerationCapability.PrivateLogistics))) != 0;

    public static bool WithinOriginalInterval(ModerationRecognitionCaptureItem item, DateTimeOffset now) =>
        now.Offset == TimeSpan.Zero && item.DroppedAt is null && now >= item.CapturedAt && now >= item.StartsAt
        && now < item.RetainUntil && (item.EndsAt is null || now < item.EndsAt);

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(Id, nameof(Id));
        ModerationHostBinding.RequireUuidV7(CaptureItemId, nameof(CaptureItemId));
        if (FreshSubjectId == Guid.Empty || SanctionId == Guid.Empty || FindingId == Guid.Empty
            || CaptureMaterialReceipt.Length != 64 || !CaptureMaterialReceipt.All(char.IsAsciiHexDigitLower)
            || BoundAt.Offset != TimeSpan.Zero || BoundAt == DateTimeOffset.MinValue
            || RetainUntil.Offset != TimeSpan.Zero || RetainUntil <= BoundAt || RetainUntil == DateTimeOffset.MaxValue)
            throw new ArgumentException("Continuity requires an exact original reference and finite retention.");
    }

    public override string ToString() => nameof(ModerationRecognitionContinuityBinding);
}
