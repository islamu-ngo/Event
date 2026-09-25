using Explore.Domain.Enums;

namespace Explore.Domain;

/// <summary>Irreversible byte-deletion authority independent of resource, tenant and audit retention.</summary>
public sealed class StorageObjectDeletionTombstone
{
    private StorageObjectDeletionTombstone() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public Guid ProviderBindingId { get; private set; }
    public string ObjectKey { get; private set; } = string.Empty;
    public string? ProviderObjectVersion { get; private set; }
    public StorageObjectDeletionState State { get; private set; }
    public Guid ConcurrencyStamp { get; private set; }
    public DateTime? NextAttemptAtUtc { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }

    public static StorageObjectDeletionTombstone Create(Guid objectId, Guid tenantId, string provider,
        Guid providerBindingId, string objectKey, string? providerObjectVersion, bool producerSettled, DateTime utcNow)
    {
        RequireUtc(utcNow);
        if (objectId == Guid.Empty || tenantId == Guid.Empty || providerBindingId == Guid.Empty
            || provider is not (StorageProviders.Local or StorageProviders.S3Compatible)
            || string.IsNullOrWhiteSpace(objectKey) || objectKey.Length > 1024 || objectKey.Any(char.IsControl)
            || Uri.TryCreate(objectKey, UriKind.Absolute, out _)
            || providerObjectVersion is { Length: 0 or > 1024 })
            throw new ArgumentException("Deletion authority requires a complete immutable provider identity.");
        return new()
        {
            Id = objectId, TenantId = tenantId, Provider = provider, ProviderBindingId = providerBindingId,
            ObjectKey = objectKey, ProviderObjectVersion = providerObjectVersion,
            State = producerSettled ? StorageObjectDeletionState.Ready : StorageObjectDeletionState.AwaitingProducer,
            ConcurrencyStamp = Guid.CreateVersion7(), NextAttemptAtUtc = producerSettled ? utcNow : null
        };
    }

    public bool TrySettleProducer(string? providerObjectVersion, DateTime utcNow)
    {
        RequireUtc(utcNow);
        if (State != StorageObjectDeletionState.AwaitingProducer) return false;
        if (providerObjectVersion is { Length: 0 or > 1024 })
            throw new ArgumentException("A provider version must be a bounded nonempty identifier.");
        ProviderObjectVersion = providerObjectVersion;
        State = StorageObjectDeletionState.Ready;
        NextAttemptAtUtc = utcNow;
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }

    public bool TryClaim(Guid expectedStamp, Guid claimStamp, DateTime utcNow, DateTime leaseExpiresAtUtc)
    {
        RequireUtc(utcNow);
        RequireUtc(leaseExpiresAtUtc);
        if (claimStamp == Guid.Empty || claimStamp == expectedStamp || leaseExpiresAtUtc <= utcNow)
            throw new ArgumentException("A fresh claim fence and bounded future lease are required.");
        bool due = State == StorageObjectDeletionState.Ready && NextAttemptAtUtc <= utcNow
            || State == StorageObjectDeletionState.Deleting && LeaseExpiresAtUtc <= utcNow;
        if (ConcurrencyStamp != expectedStamp || !due) return false;
        State = StorageObjectDeletionState.Deleting;
        ConcurrencyStamp = claimStamp;
        NextAttemptAtUtc = null;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
        return true;
    }

    public bool TryRecordAbsence(Guid claimStamp, DateTime utcNow)
    {
        RequireUtc(utcNow);
        if (!OwnsClaim(claimStamp, utcNow)) return false;
        State = StorageObjectDeletionState.Absent;
        ConcurrencyStamp = Guid.CreateVersion7();
        NextAttemptAtUtc = null;
        LeaseExpiresAtUtc = null;
        return true;
    }

    public bool TryScheduleRetry(Guid claimStamp, DateTime utcNow, DateTime nextAttemptAtUtc)
    {
        RequireUtc(utcNow);
        RequireUtc(nextAttemptAtUtc);
        if (nextAttemptAtUtc <= utcNow) throw new ArgumentException("Retry eligibility must be in the future.");
        if (!OwnsClaim(claimStamp, utcNow)) return false;
        State = StorageObjectDeletionState.Ready;
        ConcurrencyStamp = Guid.CreateVersion7();
        NextAttemptAtUtc = nextAttemptAtUtc;
        LeaseExpiresAtUtc = null;
        return true;
    }

    private bool OwnsClaim(Guid claimStamp, DateTime utcNow) =>
        State == StorageObjectDeletionState.Deleting && ConcurrencyStamp == claimStamp && LeaseExpiresAtUtc > utcNow;

    private static void RequireUtc(DateTime value)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A non-default UTC timestamp is required.");
    }

    public override string ToString() => nameof(StorageObjectDeletionTombstone);
}
