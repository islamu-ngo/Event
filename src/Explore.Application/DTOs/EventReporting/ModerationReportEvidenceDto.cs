namespace Explore.Application.DTOs.EventReporting;

public sealed record ModerationReportEvidenceDto
{
    public Guid Id { get; init; }
    public Guid ReportId { get; init; }
    public int EvidenceKindId { get; init; }
    public required string EvidenceKindCode { get; init; }
    public required string EvidenceKindName { get; init; }
    public string? TextBody { get; init; }
    public bool HasTextBody { get; init; }
    public bool IsTextUnavailable { get; init; }
    public Guid? StorageObjectId { get; init; }
    public string? ContentHash { get; init; }
    public int ClassificationId { get; init; }
    public required string ClassificationCode { get; init; }
    public required string ClassificationName { get; init; }
    public DateTime? RetentionUntilUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
