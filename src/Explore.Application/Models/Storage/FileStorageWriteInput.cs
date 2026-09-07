namespace Explore.Application.Models.Storage;

public sealed record FileStorageWriteInput(
    Guid TenantId,
    Stream Content,
    string ContentType,
    string SafeDisplayName,
    string? Extension,
    long? ExpectedSizeBytes,
    long? MaxSizeBytes,
    string? ObjectKey = null);
