using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class StorageUsageCounter : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public required string Provider { get; set; }
    public long UsedBytes { get; set; }
    public long ReservedBytes { get; set; }
    public long QuarantinedBytes { get; set; }
    public long ObjectCount { get; set; }
    public DateTime? LastRecalculatedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public bool CanReserve(long bytes, long quotaBytes)
    {
        ValidateNonNegative(bytes, nameof(bytes));
        ValidateNonNegative(quotaBytes, nameof(quotaBytes));

        return UsedBytes + ReservedBytes + bytes <= quotaBytes;
    }

    public void Reserve(long bytes, long quotaBytes)
    {
        if (!CanReserve(bytes, quotaBytes))
        {
            throw new InvalidOperationException("Storage quota would be exceeded by this reservation.");
        }

        ReservedBytes += bytes;
    }

    public void ReleaseReservation(long bytes)
    {
        ValidateNonNegative(bytes, nameof(bytes));
        ReservedBytes = Math.Max(0, ReservedBytes - bytes);
    }

    public void FinalizeReservation(long bytes)
    {
        ValidateNonNegative(bytes, nameof(bytes));
        ReservedBytes = Math.Max(0, ReservedBytes - bytes);
        UsedBytes += bytes;
        ObjectCount += 1;
    }

    public void Recalculate(long usedBytes, long reservedBytes, long quarantinedBytes, long objectCount, DateTime utcNow)
    {
        ValidateNonNegative(usedBytes, nameof(usedBytes));
        ValidateNonNegative(reservedBytes, nameof(reservedBytes));
        ValidateNonNegative(quarantinedBytes, nameof(quarantinedBytes));
        ValidateNonNegative(objectCount, nameof(objectCount));

        UsedBytes = usedBytes;
        ReservedBytes = reservedBytes;
        QuarantinedBytes = quarantinedBytes;
        ObjectCount = objectCount;
        LastRecalculatedAt = utcNow;
    }

    private static void ValidateNonNegative(long value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Storage byte counters cannot be negative.");
        }
    }
}
