using Explore.Domain.Enums;

namespace Explore.Application.Features.EventReporting.Models;

public sealed record ReviewCaseEnvelope(
    Guid TenantId,
    Guid ReportId,
    Guid EventId,
    Guid CaseId,
    Guid CaseConcurrencyStamp,
    EventReportExternalProvider Provider,
    string QueueCode,
    string CaseStatusCode,
    string PriorityCode,
    string ReasonCode,
    DateTime SubmittedAtUtc,
    DateTime? SlaDueAtUtc,
    string IdempotencyKey,
    string? CorrelationId,
    EventReportProviderEvidenceMode EvidenceMode = EventReportProviderEvidenceMode.MetadataOnly,
    EventReportProviderTargetScope ProviderTargetScope = EventReportProviderTargetScope.Instance,
    string ProviderTargetId = "instance",
    string? ProviderEndpointUrl = null,
    string? ProviderApiKey = null);

public sealed record ReviewCaseSyncResult(
    bool Succeeded,
    bool ProviderDisabled,
    bool IsRetryable,
    string? ProviderCaseId,
    string? ProviderUrl,
    EventReportProviderError? Error)
{
    public static ReviewCaseSyncResult Success(
        string? providerCaseId = null,
        string? providerUrl = null) =>
        new(true, false, false, providerCaseId, providerUrl, null);

    public static ReviewCaseSyncResult Disabled(string? safeDetail = null) =>
        new(false, true, false, null, null, new EventReportProviderError("provider_disabled", false, safeDetail));

    public static ReviewCaseSyncResult Failure(
        string category,
        bool isTransient,
        string? safeDetail = null) =>
        new(false, false, isTransient, null, null, new EventReportProviderError(category, isTransient, safeDetail));
}
