namespace Explore.Application.DTOs.StorageObject;

public sealed record StorageUploadSessionDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid? UserId { get; init; }
    public required string Provider { get; init; }
    public string RouteKey { get; init; } = "general";
    public long PolicyMaxUploadBytes { get; init; }
    public string? PolicyVersion { get; init; }
    public long ExpectedSizeBytes { get; init; }
    public long ReservedBytes { get; init; }
    public required string ContentType { get; init; }
    public string? OriginalFileName { get; init; }
    public required string SafeDisplayName { get; init; }
    public string? Extension { get; init; }
    public required string Purpose { get; init; }
    public required string Visibility { get; init; }
    public required string Status { get; init; }
    public string? IdempotencyKey { get; init; }
    public Guid? StorageObjectId { get; init; }
    public long? StoredSizeBytes { get; init; }
    public string? Sha256Checksum { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime? UploadStartedAt { get; init; }
    public DateTime? FinalizedAt { get; init; }
    public DateTime? CanceledAt { get; init; }
    public DateTime? FailedAt { get; init; }
    public long MaxUploadBytes { get; init; }
    public long TenantQuotaBytes { get; init; }
    public long UsedBytes { get; init; }
    public long TotalReservedBytes { get; init; }
}
