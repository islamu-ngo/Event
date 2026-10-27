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
    public Guid? CaseId { get; private set; }
    public Guid? SourceCaseConcurrencyStamp { get; private set; }
    public Guid? ProviderLinkId { get; private set; }
    public Guid? IncomingWebhookMessageId { get; private set; }
    public string? AdvisoryIdentity { get; private set; }
    public int? AdvisoryOrdinal { get; private set; }
    public string? AdvisoryMaterialSha256 { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    /// <summary>
    /// Records advice against an existing native dispatch binding, not a finding or decision.
    /// The material digest owns the complete callback, including omitted evidence fields.
    /// </summary>
    public static EventReportSignal CreateProviderAdvice(
        EventReport report,
        EventReportCase reportCase,
        EventReportExternalLink providerLink,
        string advisoryIdentity,
        int ordinal,
        string materialSha256,
        string signalType,
        string policyCode,
        decimal? score,
        EventReportSignalVerdict verdict,
        EventReportRecommendedAction? recommendedAction,
        string? safeSummary,
        string? externalSignalId,
        string correlationId,
        DateTime createdAt,
        IncomingWebhookMessage? incomingMessage = null)
    {
        if (reportCase.TenantId != report.TenantId || reportCase.ReportId != report.Id ||
            providerLink.TenantId != report.TenantId || providerLink.ReportId != report.Id ||
            providerLink.CaseId != reportCase.Id || providerLink.SyncState != EventReportSyncState.Synced ||
            providerLink.CaseConcurrencyStamp != reportCase.ConcurrencyStamp)
        {
            throw new ArgumentException("Provider advice requires the exact native report, case and synced dispatch binding.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);

        if (materialSha256.Length != 71 || !materialSha256.StartsWith("sha256:", StringComparison.Ordinal) ||
            materialSha256.Skip(7).Any(character => !char.IsAsciiHexDigitLower(character)))
        {
            throw new ArgumentException("Provider advice requires a SHA-256 material digest.", nameof(materialSha256));
        }

        var provider = providerLink.Provider switch
        {
            EventReportExternalProvider.Coop => EventReportSignalProvider.Coop,
            EventReportExternalProvider.Osprey => EventReportSignalProvider.Osprey,
            _ => throw new ArgumentOutOfRangeException(nameof(providerLink))
        };

        if (provider == EventReportSignalProvider.Coop &&
            (incomingMessage is null || incomingMessage.TenantId != report.TenantId ||
             incomingMessage.Provider != "coop" ||
             !string.Equals(incomingMessage.ProviderMessageId, advisoryIdentity, StringComparison.Ordinal) ||
             !string.Equals(incomingMessage.PayloadHash, materialSha256, StringComparison.Ordinal) ||
             incomingMessage.Status == IncomingWebhookMessageStatus.PayloadConflict))
        {
            throw new ArgumentException("Coop advice requires its authenticated native inbox identity.", nameof(incomingMessage));
        }

        if (provider == EventReportSignalProvider.Osprey && incomingMessage is not null)
        {
            throw new ArgumentException("Osprey HTTP advice does not have a native inbox envelope.", nameof(incomingMessage));
        }

        var signal = Create(
            report.TenantId, report.Id, report.EventId, provider, signalType, policyCode,
            score, verdict, recommendedAction, safeSummary, externalSignalId, correlationId,
            createdAt, providerLink.ProviderTargetScope, providerLink.ProviderTargetId);
        signal.CaseId = reportCase.Id;
        signal.SourceCaseConcurrencyStamp = providerLink.CaseConcurrencyStamp;
        signal.ProviderLinkId = providerLink.Id;
        signal.IncomingWebhookMessageId = incomingMessage?.Id;
        signal.AdvisoryIdentity = EventReportGuards.NormalizeRequired(
            advisoryIdentity, MaxExternalSignalIdLength, nameof(advisoryIdentity));
        signal.AdvisoryOrdinal = ordinal;
        signal.AdvisoryMaterialSha256 = materialSha256;
        return signal;
    }

    public bool MatchesAdvisorySource(
        Guid reportId,
        Guid caseId,
        Guid providerLinkId,
        string materialSha256,
        Guid? incomingWebhookMessageId,
        EventReportProviderTargetScope providerTargetScope,
        string providerTargetId,
        Guid? caseConcurrencyStamp) =>
        ReportId == reportId && CaseId == caseId && ProviderLinkId == providerLinkId &&
        caseConcurrencyStamp is not null && SourceCaseConcurrencyStamp == caseConcurrencyStamp &&
        IncomingWebhookMessageId == incomingWebhookMessageId &&
        ProviderTargetScope == providerTargetScope &&
        string.Equals(ProviderTargetId, providerTargetId, StringComparison.Ordinal) &&
        string.Equals(AdvisoryMaterialSha256, materialSha256, StringComparison.Ordinal);

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
