using Explore.Domain.Interfaces;

namespace Explore.Domain;

/// <summary>One immutable object-review clock per native cause. Neither discovery nor a retry restarts it.</summary>
public sealed class EventResourceModerationReviewDuty : ITenantEntity
{
    private Guid _tenantId;
    private EventResourceModerationReviewDuty() { }
    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(EventResourceModerationReviewDuty));
    }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(EventResourceModerationReviewDuty));
    }
    public Guid EventId { get; private set; }
    public Guid EventResourceId { get; private set; }
    public Guid MaterialVersionId { get; private set; }
    public DateTimeOffset AppliedAt { get; private set; }
    public DateTimeOffset DueAt { get; private set; }

    public static EventResourceModerationReviewDuty Discover(
        EventResourceModerationQuarantine cause, DateTimeOffset reviewAt)
    {
        cause.ValidatePersistedState();
        if (reviewAt.Offset != TimeSpan.Zero || reviewAt <= cause.AppliedAt
            || reviewAt - cause.AppliedAt > TimeSpan.FromHours(24))
            throw new ArgumentException("The original object-review deadline must be no later than 24 hours after custody.");
        var duty = new EventResourceModerationReviewDuty
        {
            Id = cause.Id, TenantId = cause.TenantId, EventId = cause.EventId,
            EventResourceId = cause.EventResourceId, MaterialVersionId = cause.MaterialVersionId,
            AppliedAt = cause.AppliedAt, DueAt = reviewAt
        };
        duty.ValidatePersistedState();
        return duty;
    }

    public bool Owns(EventResourceModerationQuarantine cause) =>
        Id == cause.Id && TenantId == cause.TenantId && EventId == cause.EventId
        && EventResourceId == cause.EventResourceId && MaterialVersionId == cause.MaterialVersionId
        && AppliedAt == cause.AppliedAt;

    public bool IsOverdueAt(DateTimeOffset now, EventResourceModerationReviewCompletion? completion)
    {
        ValidatePersistedState();
        if (now.Offset != TimeSpan.Zero || now < AppliedAt)
            throw new ArgumentException("Duty observation requires a current UTC instant.");
        if (completion is not null)
        {
            completion.ValidatePersistedState();
            if (completion.QuarantineId != Id || completion.TenantId != TenantId || completion.EventId != EventId
                || completion.EventResourceId != EventResourceId || completion.MaterialVersionId != MaterialVersionId
                || completion.CompletedAt < AppliedAt || completion.CompletedAt > now)
                throw new ArgumentException("A completed review must belong to this exact material cause.");
        }
        return now >= DueAt && completion is null;
    }

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(Id, nameof(Id));
        ModerationHostBinding.RequireUuidV7(MaterialVersionId, nameof(MaterialVersionId));
        if (TenantId == Guid.Empty || EventId == Guid.Empty || EventResourceId == Guid.Empty
            || AppliedAt.Offset != TimeSpan.Zero || AppliedAt == DateTimeOffset.MinValue
            || DueAt.Offset != TimeSpan.Zero || DueAt == DateTimeOffset.MaxValue || DueAt <= AppliedAt
            || DueAt - AppliedAt > TimeSpan.FromHours(24))
            throw new ArgumentException("A duty preserves the exact finite native object-review deadline.");
    }

    public override string ToString() => nameof(EventResourceModerationReviewDuty);
}
