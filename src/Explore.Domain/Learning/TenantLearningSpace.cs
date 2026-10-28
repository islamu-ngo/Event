using Explore.Domain.Interfaces;

namespace Explore.Domain.Learning;

public sealed class TenantLearningSpace : ITenantEntity
{
    private Guid _tenantId;
    private TenantLearningSpace() { }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(TenantLearningSpace));
    }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(TenantLearningSpace));
    }
    public string PackKey { get; private set; } = "learning-core";
    public int PackVersion { get; private set; } = 1;
    public long Revision { get; private set; }
    public bool IsRemoved { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RemovedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static TenantLearningSpace Create(Guid id, Guid tenantId, DateTime utcNow)
    {
        LearningGuard.Identity(id, nameof(id));
        LearningGuard.Identity(tenantId, nameof(tenantId));
        LearningGuard.Utc(utcNow);
        return new() { Id = id, TenantId = tenantId, CreatedAtUtc = utcNow, UpdatedAtUtc = utcNow };
    }

    public void Remove(long expectedRevision, DateTime utcNow)
    {
        LearningGuard.Mutation(Revision, expectedRevision, UpdatedAtUtc, utcNow);
        if (IsRemoved)
            throw new InvalidOperationException("The learning source is already removed.");
        IsRemoved = true;
        RemovedAtUtc = utcNow;
        UpdatedAtUtc = utcNow;
        Revision++;
    }

    public void Recreate(long expectedRevision, DateTime utcNow)
    {
        LearningGuard.Mutation(Revision, expectedRevision, UpdatedAtUtc, utcNow);
        if (!IsRemoved)
            throw new InvalidOperationException("Only a removed learning source can be recreated.");
        IsRemoved = false;
        UpdatedAtUtc = utcNow;
        Revision++;
    }
}
