namespace Explore.Application.DTOs.EventResource;

/// <summary>Only upload intent; tenant, subject, ownership, privacy and provider are server-owned.</summary>
public sealed record CreateEventResourceUploadSessionDto
{
    public required Guid ExpectedVersion { get; init; }
    public required long ExpectedSizeBytes { get; init; }
    public required string ContentType { get; init; }
    public required string SafeDisplayName { get; init; }
    public string? Extension { get; init; }
    public required string IdempotencyKey { get; init; }
    public override string ToString() => nameof(CreateEventResourceUploadSessionDto);
}
