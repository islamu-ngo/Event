namespace Explore.Persistence.Entities;

public sealed class ConfigurationDirectTransferChunk
{
    private ConfigurationDirectTransferChunk()
    {
    }

    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public int Offset { get; private set; }
    public int ByteLength { get; private set; }
    public string Digest { get; private set; } = string.Empty;
    public byte[] ProtectedPayload { get; private set; } = [];
    public DateTime ExpiresAt { get; private set; }

    public static ConfigurationDirectTransferChunk Create(
        Guid id,
        Guid sessionId,
        int offset,
        int byteLength,
        string digest,
        byte[] protectedPayload,
        DateTime expiresAt) =>
        new()
        {
            Id = id,
            SessionId = sessionId,
            Offset = offset,
            ByteLength = byteLength,
            Digest = digest,
            ProtectedPayload = protectedPayload,
            ExpiresAt = expiresAt
        };
}
