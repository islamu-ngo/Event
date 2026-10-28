using Explore.Domain.Interfaces;

namespace Explore.Domain;

/// <summary>
/// Immutable operational release receipt. Historical cause coordinates survive private-purpose
/// expiry; neither this receipt nor a fresh review reconstructs or reverses the original finding.
/// </summary>
public sealed class EventResourceModerationQuarantineRelease : ITenantEntity
{
    private Guid _tenantId;
    public required Guid Id { get; init; }
    public required Guid TenantId
    {
        get => _tenantId;
        init => TenantIdentity.Set(ref _tenantId, value, nameof(EventResourceModerationQuarantineRelease));
    }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(EventResourceModerationQuarantineRelease));
    }
    public required Guid QuarantineId { get; init; }
    public required Guid EventId { get; init; }
    public required Guid EventResourceId { get; init; }
    public required Guid MaterialVersionId { get; init; }
    public required Guid OriginalEffectId { get; init; }
    public required int OriginalBindingOrdinal { get; init; }
    public required DateTimeOffset OriginallyAppliedAt { get; init; }
    public required Guid ApprovalId { get; init; }
    public required Guid ReleasedByHumanId { get; init; }
    public required DateTimeOffset ReleasedAt { get; init; }
    public required string RequestDigest { get; init; }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, QuarantineId, MaterialVersionId, OriginalEffectId, ApprovalId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (TenantId == Guid.Empty || EventId == Guid.Empty || EventResourceId == Guid.Empty
            || ReleasedByHumanId == Guid.Empty || OriginalBindingOrdinal is < 0 or >= 64
            || OriginallyAppliedAt.Offset != TimeSpan.Zero || OriginallyAppliedAt == DateTimeOffset.MinValue
            || ReleasedAt.Offset != TimeSpan.Zero || ReleasedAt == DateTimeOffset.MaxValue
            || ReleasedAt < OriginallyAppliedAt || !EventResourceModerationQuarantineReview.Digest(RequestDigest))
            throw new ArgumentException("Release requires exact original cause custody and an immutable transition.");
    }

    public bool Owns(EventResourceModerationQuarantine cause) =>
        QuarantineId == cause.Id && TenantId == cause.TenantId && EventId == cause.EventId
        && EventResourceId == cause.EventResourceId && MaterialVersionId == cause.MaterialVersionId
        && OriginalEffectId == cause.EffectId && OriginalBindingOrdinal == cause.BindingOrdinal
        && OriginallyAppliedAt == cause.AppliedAt;

    public override string ToString() => nameof(EventResourceModerationQuarantineRelease);
}
