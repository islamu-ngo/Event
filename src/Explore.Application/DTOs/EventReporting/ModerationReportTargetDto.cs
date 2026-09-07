namespace Explore.Application.DTOs.EventReporting;

public sealed record ModerationReportTargetDto
{
    public Guid Id { get; init; }
    public Guid ReportId { get; init; }
    public int TargetKindId { get; init; }
    public required string TargetKindCode { get; init; }
    public required string TargetKindName { get; init; }
    public Guid TargetId { get; init; }
    public string? FieldPath { get; init; }
    public Guid? StorageObjectId { get; init; }
}
