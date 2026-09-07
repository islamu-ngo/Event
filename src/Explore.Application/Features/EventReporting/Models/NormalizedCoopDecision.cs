using Explore.Domain.Enums;

namespace Explore.Application.Features.EventReporting.Models;

public sealed record NormalizedCoopDecision(
    Guid TenantId,
    Guid EventId,
    Guid ReportId,
    Guid CaseId,
    Guid? ExpectedCaseConcurrencyStamp,
    EventReportDecisionKind DecisionKind,
    string ReasonCode,
    string? SafeNote,
    Guid? DuplicateGroupId,
    string ExternalDecisionId,
    string? ProviderCaseId,
    string? ProviderUrl,
    EventReportProviderTargetScope ProviderTargetScope,
    string ProviderTargetId,
    string CorrelationId);
