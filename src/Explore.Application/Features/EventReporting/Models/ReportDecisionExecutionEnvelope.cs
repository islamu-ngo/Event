using Explore.Domain.Enums;

namespace Explore.Application.Features.EventReporting.Models;

public sealed record ReportDecisionExecutionEnvelope(
    Guid TenantId,
    Guid EventId,
    Guid ReportId,
    Guid CaseId,
    Guid DecisionId,
    EventReportDecisionKind DecisionKind,
    string ReasonCode,
    string? SafeNote,
    string? ExternalDecisionId,
    string IdempotencyKey,
    string? CorrelationId);

public sealed record ReportDecisionExecutionResult(
    bool Succeeded,
    bool AlreadyExecuted,
    bool IsRetryable,
    Guid? ModerationRecordId,
    EventReportProviderError? Error)
{
    public static ReportDecisionExecutionResult Success(Guid? moderationRecordId = null) =>
        new(true, false, false, moderationRecordId, null);

    public static ReportDecisionExecutionResult AlreadyComplete(Guid? moderationRecordId = null) =>
        new(true, true, false, moderationRecordId, null);

    public static ReportDecisionExecutionResult Failure(
        string category,
        bool isTransient,
        string? safeDetail = null) =>
        new(false, false, isTransient, null, new EventReportProviderError(category, isTransient, safeDetail));
}
