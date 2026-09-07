namespace Explore.Persistence.Entities;

public sealed class ConfigurationImportStoredArtifact
{
    private ConfigurationImportStoredArtifact()
    {
    }

    public Guid Id { get; private set; }
    public byte[] ProtectedPayload { get; private set; } = [];
    public string Sha256Digest { get; private set; } = string.Empty;
    public int ByteLength { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    public static ConfigurationImportStoredArtifact Create(
        Guid id,
        byte[] protectedPayload,
        string sha256Digest,
        int byteLength,
        DateTime createdAt,
        DateTime expiresAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentNullException.ThrowIfNull(protectedPayload);
        if (protectedPayload.Length == 0)
            throw new ArgumentException("Protected payload is required.", nameof(protectedPayload));
        return new ConfigurationImportStoredArtifact
        {
            Id = id,
            ProtectedPayload = protectedPayload.ToArray(),
            Sha256Digest = sha256Digest,
            ByteLength = byteLength,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt
        };
    }
}
