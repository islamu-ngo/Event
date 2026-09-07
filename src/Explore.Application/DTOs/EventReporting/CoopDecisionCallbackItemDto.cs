using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.EventReporting;

public sealed record CoopDecisionCallbackItemDto
{
    public string? Id { get; init; }
    public string? TypeId { get; init; }

    [JsonPropertyName("type_id")]
    public string? TypeIdSnake { get; init; }

    public Guid TenantId { get; init; }

    [JsonPropertyName("tenant_id")]
    public Guid TenantIdSnake { get; init; }

    public Guid ReportId { get; init; }

    [JsonPropertyName("report_id")]
    public Guid ReportIdSnake { get; init; }

    public Guid EventId { get; init; }

    [JsonPropertyName("event_id")]
    public Guid EventIdSnake { get; init; }

    public Guid CaseId { get; init; }

    [JsonPropertyName("case_id")]
    public Guid CaseIdSnake { get; init; }
}
