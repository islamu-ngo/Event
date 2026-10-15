using Explore.Domain.Interfaces;

namespace Explore.Domain;

/// <summary>
/// Immutable fact that the native fresh review owner committed an inspected, independently
/// approved disposition. It discharges the object-review duty, not the material guard.
/// Expired evidence cannot authorize release; expiry does not undo a completed review.
/// </summary>
public sealed class EventResourceModerationReviewCompletion : ITenantEntity
{
    private Guid _tenantId;
    private EventResourceModerationReviewCompletion() { }
    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(EventResourceModerationReviewCompletion));
    }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(EventResourceModerationReviewCompletion));
    }
    public Guid QuarantineId { get; private set; }
    public Guid EventId { get; private set; }
    public Guid EventResourceId { get; private set; }
    public Guid MaterialVersionId { get; private set; }
    public Guid PurposeId { get; private set; }
    public Guid AttestationId { get; private set; }
    public Guid ApprovalId { get; private set; }
    public ResourceSafetyAttestation Disposition { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }

    public static EventResourceModerationReviewCompletion Complete(
        EventResourceModerationQuarantine cause, IReadOnlyList<EventResourceModerationQuarantineReview> chain)
    {
        cause.ValidatePersistedState();
        if (chain.Count is < 4 or > 5 || chain.Count != 3 + chain[0].RequiredApprovals
            || chain.Select(row => row.Id).Distinct().Count() != chain.Count || cause.ReleaseId is not null)
            throw new ArgumentException("Only a complete fresh inspection and approval quorum completes review.");
        for (var index = 0; index < chain.Count; index++)
        {
            var row = chain[index];
            row.ValidatePersistedState();
            var expected = index switch
            {
                0 => ResourceSafetyReviewStep.ProposePurpose,
                1 => ResourceSafetyReviewStep.AdoptPurpose,
                2 => ResourceSafetyReviewStep.AttestMaterialSafety,
                _ => chain[2].Attestation == ResourceSafetyAttestation.IndependentlyInspectedUnsafeForCurrentAudience
                    ? ResourceSafetyReviewStep.ApproveContinuedQuarantine : ResourceSafetyReviewStep.ApproveSafety
            };
            if (!row.Owns(cause) || row.Step != expected || row.PurposeId != chain[0].Id
                || row.HostBindingId != chain[0].HostBindingId || row.TrustDomainId != chain[0].TrustDomainId
                || row.CaseId != chain[0].CaseId || row.CaseRevision != chain[0].CaseRevision
                || row.ResourceStamp != chain[0].ResourceStamp
                || row.RequiredApprovals != chain[0].RequiredApprovals
                || row.MaximumEvidenceLifetimeTicks != chain[0].MaximumEvidenceLifetimeTicks
                || row.NativeDigest != chain[0].NativeDigest || row.RecordedAt < cause.AppliedAt
                || row.RecordedAt > chain[^1].RecordedAt || row.ValidUntil <= chain[^1].RecordedAt
                || index > 0 && (row.PredecessorId != chain[index - 1].Id
                    || row.RecordedAt < chain[index - 1].RecordedAt
                    || row.ValidUntil > chain[index - 1].ValidUntil))
                throw new ArgumentException("Review completion cannot borrow evidence or partial approvals.");
        }
        if (!Independent(chain[0], chain[1])
            || Enumerable.Range(3, chain.Count - 3).Any(index => chain.Skip(2).Take(index - 2)
                .Any(prior => !Independent(prior, chain[index]))))
            throw new ArgumentException("The disposition requires independent controllers.");
        var result = new EventResourceModerationReviewCompletion
        {
            Id = chain[^1].Id, TenantId = cause.TenantId, QuarantineId = cause.Id,
            EventId = cause.EventId, EventResourceId = cause.EventResourceId, MaterialVersionId = cause.MaterialVersionId,
            PurposeId = chain[0].Id, AttestationId = chain[2].Id, ApprovalId = chain[^1].Id,
            Disposition = chain[2].Attestation!.Value, CompletedAt = chain[^1].RecordedAt
        };
        result.ValidatePersistedState();
        return result;
    }

    public bool Owns(EventResourceModerationQuarantine cause) =>
        TenantId == cause.TenantId && QuarantineId == cause.Id && EventId == cause.EventId
        && EventResourceId == cause.EventResourceId && MaterialVersionId == cause.MaterialVersionId;

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, QuarantineId, MaterialVersionId, PurposeId, AttestationId, ApprovalId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (Id != ApprovalId || TenantId == Guid.Empty || EventId == Guid.Empty || EventResourceId == Guid.Empty
            || !Enum.IsDefined(Disposition) || CompletedAt.Offset != TimeSpan.Zero
            || CompletedAt == DateTimeOffset.MinValue || CompletedAt == DateTimeOffset.MaxValue)
            throw new ArgumentException("Review completion requires exact opaque custody and a native completion instant.");
    }

    private static bool Independent(EventResourceModerationQuarantineReview left, EventResourceModerationQuarantineReview right) =>
        left.HumanId != right.HumanId && left.ControllerHumanId != right.ControllerHumanId
        && left.HumanId != right.ControllerHumanId && left.ControllerHumanId != right.HumanId;
    public override string ToString() => nameof(EventResourceModerationReviewCompletion);
}
