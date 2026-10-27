using Explore.Domain.Interfaces;

namespace Explore.Domain;

/// <summary>
/// Receipt for one atomically materialized native manager escalation. Its finite purpose is
/// solely a content-free overdue-review notification, never inspection, release or old-R renewal.
/// </summary>
public sealed class EventResourceModerationReviewEscalation : ITenantEntity
{
    private Guid _tenantId;
    public required Guid Id { get; init; }
    public required Guid TenantId
    {
        get => _tenantId;
        init => TenantIdentity.Set(ref _tenantId, value, nameof(EventResourceModerationReviewEscalation));
    }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(EventResourceModerationReviewEscalation));
    }
    public required Guid HostBindingId { get; init; }
    public required Guid ServicePrincipalId { get; init; }
    public required long QualificationRevision { get; init; }
    public required DateTimeOffset EscalatedAt { get; init; }
    public required DateTimeOffset PurposeUntil { get; init; }
    public required int RecipientCount { get; init; }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, ServicePrincipalId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (TenantId == Guid.Empty || QualificationRevision < 1 || RecipientCount is < 1 or > 32
            || EscalatedAt.Offset != TimeSpan.Zero || PurposeUntil.Offset != TimeSpan.Zero
            || EscalatedAt == DateTimeOffset.MinValue || PurposeUntil == DateTimeOffset.MaxValue
            || PurposeUntil <= EscalatedAt || PurposeUntil - EscalatedAt > TimeSpan.FromMinutes(5))
            throw new ArgumentException("Escalation needs current native authority, bounded recipients and a finite purpose.");
    }
    public override string ToString() => nameof(EventResourceModerationReviewEscalation);
}
