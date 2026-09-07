namespace Explore.Application.Models.InternalEvents;

public sealed record EventReportProviderSyncRequested
{
    public required Guid TenantId { get; init; }
    public required Guid ReportId { get; init; }
    public required Guid EventId { get; init; }
    public required Guid CaseId { get; init; }
    public required Guid CaseConcurrencyStamp { get; init; }
    public required string ReasonCode { get; init; }
    public required string QueueCode { get; init; }
    public required DateTime SubmittedAtUtc { get; init; }
    public string? CorrelationId { get; init; }
}
