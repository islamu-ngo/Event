using Explore.Domain.Interfaces;

namespace Explore.Domain;

/// <summary>
/// An operational cause of the resource's material quarantine, not a copy of the original
/// notice or an authorization grant. Each original effect ordinal owns a distinct cause.
/// Only the resource creates this receipt alongside its persisted delivery guard.
/// No expiry, personal-sanction reversal or ordinary resource edit releases this control.
/// </summary>
public sealed class EventResourceModerationQuarantine : ITenantEntity
{
    private Guid _tenantId;
    private EventResourceModerationQuarantine() { }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(EventResourceModerationQuarantine));
    }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(EventResourceModerationQuarantine));
    }
    public Guid EventId { get; private set; }
    public Guid EventResourceId { get; private set; }
    public Guid MaterialVersionId { get; private set; }
    public Guid EffectId { get; private set; }
    public int BindingOrdinal { get; private set; }
    public DateTimeOffset AppliedAt { get; private set; }
    public Guid? ReleaseId { get; private set; }

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(Id, nameof(Id));
        ModerationHostBinding.RequireUuidV7(EffectId, nameof(EffectId));
        ModerationHostBinding.RequireUuidV7(MaterialVersionId, nameof(MaterialVersionId));
        if (ReleaseId is { } releaseId) ModerationHostBinding.RequireUuidV7(releaseId, nameof(ReleaseId));
        if (TenantId == Guid.Empty || EventId == Guid.Empty || EventResourceId == Guid.Empty
            || BindingOrdinal is < 0 or >= 64 || AppliedAt.Offset != TimeSpan.Zero
            || AppliedAt == DateTimeOffset.MinValue || AppliedAt == DateTimeOffset.MaxValue)
            throw new ArgumentException("A quarantine receipt requires exact native ownership and a UTC transition.");
    }

    internal void Release(EventResourceModerationQuarantineRelease receipt)
    {
        receipt.ValidatePersistedState();
        if (!receipt.Owns(this) || ReleaseId is not null && ReleaseId != receipt.Id)
            throw new InvalidOperationException("Only the exact immutable release may retire this cause.");
        ReleaseId = receipt.Id;
    }

    internal static EventResourceModerationQuarantine Create(
        Guid id, EventResource resource, Guid effectId, int bindingOrdinal, DateTimeOffset appliedAt)
    {
        ModerationHostBinding.RequireUuidV7(id, nameof(id));
        ModerationHostBinding.RequireUuidV7(effectId, nameof(effectId));
        if (bindingOrdinal is < 0 or >= 64)
            throw new ArgumentOutOfRangeException(nameof(bindingOrdinal));
        if (appliedAt.Offset != TimeSpan.Zero || appliedAt == DateTimeOffset.MinValue
            || appliedAt == DateTimeOffset.MaxValue || appliedAt.UtcDateTime < resource.CreatedAt)
            throw new ArgumentException("A quarantine transition requires a current UTC instant.", nameof(appliedAt));
        if (resource.MaterialVersionId is not { } materialVersionId)
            throw new InvalidOperationException("A quarantine cause must bind native material.");
        var receipt = new EventResourceModerationQuarantine
        {
            Id = id,
            TenantId = resource.TenantId,
            EventId = resource.EventId,
            EventResourceId = resource.Id,
            MaterialVersionId = materialVersionId,
            EffectId = effectId,
            BindingOrdinal = bindingOrdinal,
            AppliedAt = appliedAt
        };
        receipt.ValidatePersistedState();
        return receipt;
    }

    public override string ToString() => nameof(EventResourceModerationQuarantine);
}
