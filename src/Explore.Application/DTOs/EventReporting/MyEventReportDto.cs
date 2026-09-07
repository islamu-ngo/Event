namespace Explore.Application.DTOs.EventReporting;

public sealed record MyEventReportDto
{
    public Guid Id { get; init; }
    public Guid EventId { get; init; }
    public int StatusId { get; init; }
    public required string StatusCode { get; init; }
    public required string StatusName { get; init; }
    public int? ReasonId { get; init; }
    public required string ReasonCode { get; init; }
    public required string ReasonName { get; init; }
    public string? SubcategoryCode { get; init; }
    public DateTime SubmittedAtUtc { get; init; }
    public DateTime? LastUpdatedAtUtc { get; init; }
    public DateTime? ClosedAtUtc { get; init; }
    public required bool ReportCaseUpdatesConsent { get; init; }
    public required bool ReportFollowUpContactConsent { get; init; }
}
