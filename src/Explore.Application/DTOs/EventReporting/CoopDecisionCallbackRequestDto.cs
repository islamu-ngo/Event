using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.EventReporting;

public sealed record CoopDecisionCallbackRequestDto
{
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

    public Guid? ExpectedCaseConcurrencyStamp { get; init; }

    [JsonPropertyName("expected_case_concurrency_stamp")]
    public Guid? ExpectedCaseConcurrencyStampSnake { get; init; }

    public Guid? DuplicateGroupId { get; init; }

    [JsonPropertyName("duplicate_group_id")]
    public Guid? DuplicateGroupIdSnake { get; init; }

    public string? ProviderDecisionId { get; init; }

    [JsonPropertyName("provider_decision_id")]
    public string? ProviderDecisionIdSnake { get; init; }

    public string? ProviderCaseId { get; init; }

    [JsonPropertyName("provider_case_id")]
    public string? ProviderCaseIdSnake { get; init; }

    public string? ProviderUrl { get; init; }

    [JsonPropertyName("provider_url")]
    public string? ProviderUrlSnake { get; init; }

    public string? ProviderTargetScope { get; init; }

    [JsonPropertyName("provider_target_scope")]
    public string? ProviderTargetScopeSnake { get; init; }

    public string? ProviderTargetId { get; init; }

    [JsonPropertyName("provider_target_id")]
    public string? ProviderTargetIdSnake { get; init; }

    public string? CorrelationId { get; init; }

    [JsonPropertyName("correlation_id")]
    public string? CorrelationIdSnake { get; init; }

    public string? ReasonCode { get; init; }

    [JsonPropertyName("reason_code")]
    public string? ReasonCodeSnake { get; init; }

    public string? SafeNote { get; init; }

    [JsonPropertyName("safe_note")]
    public string? SafeNoteSnake { get; init; }

    public CoopDecisionCallbackItemDto? Item { get; init; }
    public CoopDecisionCallbackActionDto? Action { get; init; }
    public IReadOnlyList<CoopDecisionCallbackPolicyDto> Policies { get; init; } = [];
    public IReadOnlyList<CoopDecisionCallbackRuleDto> Rules { get; init; } = [];
}
