using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.EventReporting;

public sealed record ModerationReportCaseDto
{
    public Guid Id { get; init; }
    public Guid ReportId { get; init; }
    public required string QueueCode { get; init; }
    public int StatusId { get; init; }
    public required string StatusCode { get; init; }
    public required string StatusName { get; init; }
    public int PriorityId { get; init; }
    public required string PriorityCode { get; init; }
    public required string PriorityName { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? AssignedModeratorUserId { get; init; }
    public DateTime? SlaDueAtUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? LastUpdatedAtUtc { get; init; }
    public Guid ConcurrencyStamp { get; init; }
}
