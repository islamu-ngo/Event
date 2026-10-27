using Explore.Domain.Interfaces;

namespace Explore.Domain;

public enum ResourceSafetyReviewStep
{
    ProposePurpose = 1,
    AdoptPurpose = 2,
    AttestMaterialSafety = 3,
    ApproveSafety = 4,
    WithdrawPurpose = 5,
    ApproveContinuedQuarantine = 6
}

/// <summary>
/// A qualified human's fresh inspection of the current material and audience. This is neither
/// urgency nor a response to the historical allegation. No original private evidence is copied.
/// </summary>
public enum ResourceSafetyAttestation
{
    IndependentlyInspectedSafeForCurrentAudience = 1,
    IndependentlyInspectedUnsafeForCurrentAudience = 2
}

/// <summary>
/// Immutable, native-authored proof in a per-cause review chain: purpose, independent adoption,
/// fresh human inspection and independent approval(s). The purpose authorizes material-safety
/// review and either continued quarantine or safe release of this cause. It cannot reverse a finding.
/// This stores a minimized human attestation, not inspected bytes or an unbounded private narrative.
/// </summary>
public sealed class EventResourceModerationQuarantineReview : ITenantEntity
{
    private Guid _tenantId;
    public required Guid Id { get; init; }
    public required Guid TenantId
    {
        get => _tenantId;
        init => TenantIdentity.Set(ref _tenantId, value, nameof(EventResourceModerationQuarantineReview));
    }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(EventResourceModerationQuarantineReview));
    }
    public required Guid HostBindingId { get; init; }
    public required Guid TrustDomainId { get; init; }
    public required Guid QuarantineId { get; init; }
    public required Guid EventId { get; init; }
    public required Guid EventResourceId { get; init; }
    public required Guid MaterialVersionId { get; init; }
    public required Guid CaseId { get; init; }
    public required long CaseRevision { get; init; }
    public required ResourceSafetyReviewStep Step { get; init; }
    public required Guid PurposeId { get; init; }
    public required Guid? PredecessorId { get; init; }
    public required Guid HumanId { get; init; }
    public required Guid ReviewerAssignmentId { get; init; }
    public required long ReviewerRevision { get; init; }
    public required Guid ControllerHumanId { get; init; }
    public required int RequiredApprovals { get; init; }
    public required long MaximumEvidenceLifetimeTicks { get; init; }
    public required ResourceSafetyAttestation? Attestation { get; init; }
    public required Guid ResourceStamp { get; init; }
    public required string NativeDigest { get; init; }
    public required string RequestDigest { get; init; }
    public required long QualificationRevision { get; init; }
    public required DateTimeOffset RecordedAt { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, TenantId, HostBindingId, TrustDomainId, QuarantineId,
                     MaterialVersionId, CaseId, PurposeId, ReviewerAssignmentId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (PredecessorId is { } predecessorId)
            ModerationHostBinding.RequireUuidV7(predecessorId, nameof(PredecessorId));
        if (EventId == Guid.Empty || EventResourceId == Guid.Empty || HumanId == Guid.Empty
            || ControllerHumanId == Guid.Empty || ResourceStamp == Guid.Empty || CaseRevision < 1
            || ReviewerRevision < 3 || QualificationRevision < 1 || !Enum.IsDefined(Step)
            || RequiredApprovals is < 1 or > 2
            || MaximumEvidenceLifetimeTicks is <= 0 or long.MaxValue
            || !Digest(NativeDigest) || !Digest(RequestDigest)
            || RecordedAt.Offset != TimeSpan.Zero || RecordedAt == DateTimeOffset.MinValue
            || ValidUntil.Offset != TimeSpan.Zero || ValidUntil == DateTimeOffset.MaxValue
            || ValidUntil <= RecordedAt
            || (Step == ResourceSafetyReviewStep.ProposePurpose
                ? PurposeId != Id || PredecessorId is not null
                : PurposeId == Id || PredecessorId is null || PredecessorId == Id || PredecessorId == Guid.Empty)
            || (Step == ResourceSafetyReviewStep.AttestMaterialSafety
                ? Attestation is not { } attestation || !Enum.IsDefined(attestation)
                    || ValidUntil - RecordedAt > TimeSpan.FromTicks(MaximumEvidenceLifetimeTicks)
                : Attestation is not null))
            throw new ArgumentException("Safety review requires exact custody, a closed purpose and finite qualified proof.");
    }

    public bool Owns(EventResourceModerationQuarantine cause) =>
        QuarantineId == cause.Id && TenantId == cause.TenantId && EventId == cause.EventId
        && EventResourceId == cause.EventResourceId && MaterialVersionId == cause.MaterialVersionId;

    public static bool Digest(string value) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');

    public override string ToString() => nameof(EventResourceModerationQuarantineReview);
}
