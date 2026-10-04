namespace Explore.Domain;

/// <summary>Durable non-session producer identity, transferred atomically to metadata or a tombstone.</summary>
public sealed class StorageProducerOperation
{
    private StorageProducerOperation() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ProviderBindingId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string ObjectKey { get; private set; } = string.Empty;
    public string? ProviderVersionId { get; private set; }
    public bool ProducerSettled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid ConcurrencyStamp { get; private set; }

    public static StorageProducerOperation Create(Guid id, Guid tenantId, StorageProviderBinding binding,
        string objectKey, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(binding);
        // The same identity rules apply before a write and after cleanup takes ownership.
        _ = StorageObjectDeletionTombstone.Create(id, tenantId, binding.Provider, binding.Id,
            objectKey, null, false, utcNow);
        return new()
        {
            Id = id,
            TenantId = tenantId,
            ProviderBindingId = binding.Id,
            Provider = binding.Provider,
            ObjectKey = objectKey,
            CreatedAtUtc = utcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    public void Settle(Guid bindingId, string provider, string objectKey, string? providerVersionId)
    {
        if (bindingId != ProviderBindingId || provider != Provider || objectKey != ObjectKey
            || providerVersionId is { Length: 0 or > 1024 }
            || ProducerSettled && ProviderVersionId != providerVersionId)
            throw new InvalidOperationException("Producer settlement requires its exact captured target.");
        ProviderVersionId = providerVersionId;
        ProducerSettled = true;
    }

    public StorageObjectDeletionTombstone Retire(DateTime utcNow) =>
        StorageObjectDeletionTombstone.Create(Id, TenantId, Provider, ProviderBindingId, ObjectKey,
            ProviderVersionId, ProducerSettled, utcNow);
}
