namespace Explore.Domain;

/// <summary>Captures one exact target without treating active metadata as acknowledgement of surviving custody.</summary>
public static class StorageRetirementTarget
{
    public static StorageObjectDeletionTombstone Capture(StorageObject source,
        IReadOnlyCollection<StorageUploadSession> sessions, StorageProducerOperation? operation, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.StorageProviderBindingId is not { } bindingId || source.ObjectKey is not { } key
            || sessions.Count > 1 || sessions.Count != 0 && operation is not null)
            throw new ArgumentException("Retirement requires one complete captured producer target.");

        string? version = source.ProviderVersionId;
        foreach (var session in sessions)
        {
            if (session.TenantId != source.TenantId
                || (session.StorageObjectId ?? session.Id) != source.Id
                || session.Provider != source.Provider || session.StorageProviderBindingId != bindingId
                || session.ObjectKey != key)
                throw new ArgumentException("Retirement custody does not match its source.");
            MergeVersion(session.ProviderVersionId);
        }
        if (operation is not null)
        {
            if (operation.Id != source.Id || operation.TenantId != source.TenantId
                || operation.Provider != source.Provider || operation.ProviderBindingId != bindingId
                || operation.ObjectKey != key)
                throw new ArgumentException("Retirement custody does not match its source.");
            MergeVersion(operation.ProviderVersionId);
        }

        bool settled = sessions.Count != 0 ? sessions.All(session => session.ProducerSettled)
            : operation is not null ? operation.ProducerSettled
            : source.LifecycleState == StorageObjectLifecycleStates.Active;
        return StorageObjectDeletionTombstone.Create(source.Id, source.TenantId, source.Provider,
            bindingId, key, version, settled, utcNow);

        void MergeVersion(string? knownVersion)
        {
            if (version is not null && knownVersion is not null && version != knownVersion)
                throw new ArgumentException("Retirement custody versions disagree.");
            version ??= knownVersion;
        }
    }
}
