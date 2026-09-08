using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventReportSignal : ITenantEntity, IAuditableEntity
{
    private const int MaxSignalTypeLength = 100;
    private const int MaxPolicyCodeLength = 100;
    private const int MaxSafeSummaryLength = 500;
    private const int MaxExternalSignalIdLength = 200;
    private const int MaxProviderTargetIdLength = 200;
    private const int MaxCorrelationIdLength = 100;

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; private set; } = null!;
    public Guid? ReportId { get; private set; }
    public EventReport? Report { get; private set; }
    public Guid EventId { get; private set; }
    public Event Event { get; private set; } = null!;
    public EventReportSignalProvider Provider { get; private set; }
    public EventReportProviderTargetScope ProviderTargetScope { get; private set; }
    public string ProviderTargetId { get; private set; } = null!;
    public string SignalType { get; private set; } = null!;
    public string PolicyCode { get; private set; } = null!;
    public decimal? Score { get; private set; }
    public EventReportSignalVerdict Verdict { get; private set; }
    public EventReportRecommendedAction? RecommendedAction { get; private set; }
    public string? SafeSummary { get; private set; }
    public string? ExternalSignalId { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static EventReportSignal Create(
        Guid tenantId,
        Guid? reportId,
        Guid eventId,
        EventReportSignalProvider provider,
        string signalType,
        string policyCode,
        decimal? score,
        EventReportSignalVerdict verdict,
        EventReportRecommendedAction? recommendedAction,
        string? safeSummary,
        string? externalSignalId,
        string correlationId,
        DateTime? createdAt = null,
        EventReportProviderTargetScope providerTargetScope = EventReportProviderTargetScope.Instance,
        string providerTargetId = "instance")
    {
        EventReportGuards.RequireGuid(tenantId, nameof(tenantId), "Tenant id is required.");
        EventReportGuards.RequireGuid(eventId, nameof(eventId), "Event id is required.");
        EventReportGuards.RequireDefined(provider, nameof(provider));
        EventReportGuards.RequireDefined(providerTargetScope, nameof(providerTargetScope));
        EventReportGuards.RequireDefined(verdict, nameof(verdict));
        EventReportGuards.RequireScore(score, nameof(score));

        if (reportId == Guid.Empty)
        {
            throw new ArgumentException("Report id cannot be empty.", nameof(reportId));
        }

        if (recommendedAction is not null)
        {
            EventReportGuards.RequireDefined(recommendedAction.Value, nameof(recommendedAction));
        }

        return new EventReportSignal
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ReportId = reportId,
            EventId = eventId,
            Provider = provider,
            ProviderTargetScope = providerTargetScope,
            ProviderTargetId = EventReportGuards.NormalizeRequired(providerTargetId, MaxProviderTargetIdLength, nameof(providerTargetId)),
            SignalType = EventReportGuards.NormalizeRequired(signalType, MaxSignalTypeLength, nameof(signalType)),
            PolicyCode = EventReportGuards.NormalizeRequired(policyCode, MaxPolicyCodeLength, nameof(policyCode)),
            Score = score,
            Verdict = verdict,
            RecommendedAction = recommendedAction,
            SafeSummary = EventReportGuards.NormalizeOptional(safeSummary, MaxSafeSummaryLength, nameof(safeSummary)),
            ExternalSignalId = EventReportGuards.NormalizeOptional(externalSignalId, MaxExternalSignalIdLength, nameof(externalSignalId)),
            CorrelationId = EventReportGuards.NormalizeRequired(correlationId, MaxCorrelationIdLength, nameof(correlationId)),
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
    }
}
