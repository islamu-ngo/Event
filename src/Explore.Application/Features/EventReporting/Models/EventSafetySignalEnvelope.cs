using Explore.Domain.Enums;

namespace Explore.Application.Features.EventReporting.Models;

public sealed record EventSafetySignalEnvelope(
    Guid TenantId,
    Guid? ReportId,
    Guid EventId,
    EventReportSignalProvider Provider,
    string SignalType,
    string PolicyCode,
    decimal? Score,
    EventReportSignalVerdict Verdict,
    EventReportRecommendedAction? RecommendedAction,
    string? SafeSummary,
    string? ExternalSignalId,
    string CorrelationId,
    DateTime CreatedAtUtc,
    EventReportProviderTargetScope ProviderTargetScope = EventReportProviderTargetScope.Instance,
    string ProviderTargetId = "instance");

public sealed record EventSafetySignalProviderResult(
    bool Succeeded,
    IReadOnlyList<EventSafetySignalEnvelope> Signals,
    EventReportProviderError? Error)
{
    public static EventSafetySignalProviderResult Success(IReadOnlyList<EventSafetySignalEnvelope>? signals = null) =>
        new(true, signals ?? [], null);

    public static EventSafetySignalProviderResult Failure(
        string category,
        bool isTransient,
        string? safeDetail = null) =>
        new(false, [], new EventReportProviderError(category, isTransient, safeDetail));
}
