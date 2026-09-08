using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventReportExternalLink : ITenantEntity, IAuditableEntity
{
    private const int MaxProviderIdLength = 200;
    private const int MaxProviderTargetIdLength = 200;
    private const int MaxProviderUrlLength = 500;
    private const int MaxErrorCategoryLength = 100;
    private const int MaxCorrelationIdLength = 100;

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; private set; } = null!;
    public Guid ReportId { get; private set; }
    public EventReport Report { get; private set; } = null!;
    public Guid? CaseId { get; private set; }
    public EventReportCase? Case { get; private set; }
    public EventReportExternalProvider Provider { get; private set; }
    public EventReportProviderTargetScope ProviderTargetScope { get; private set; }
    public string ProviderTargetId { get; private set; } = null!;
    public string? ProviderCaseId { get; private set; }
    public string? ProviderSignalId { get; private set; }
    public string? ProviderUrl { get; private set; }
    public EventReportSyncState SyncState { get; private set; }
    public DateTime? LastSyncedAt { get; private set; }
    public string? LastErrorCategory { get; private set; }
    public int RetryCount { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static EventReportExternalLink CreatePending(
        Guid tenantId,
        Guid reportId,
        Guid? caseId,
        EventReportExternalProvider provider,
        string correlationId,
        DateTime? createdAt = null,
        EventReportProviderTargetScope providerTargetScope = EventReportProviderTargetScope.Instance,
        string providerTargetId = "instance")
    {
        EventReportGuards.RequireGuid(tenantId, nameof(tenantId), "Tenant id is required.");
        EventReportGuards.RequireGuid(reportId, nameof(reportId), "Report id is required.");
        EventReportGuards.RequireDefined(provider, nameof(provider));
        EventReportGuards.RequireDefined(providerTargetScope, nameof(providerTargetScope));

        if (caseId == Guid.Empty)
        {
            throw new ArgumentException("Case id cannot be empty.", nameof(caseId));
        }

        return new EventReportExternalLink
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ReportId = reportId,
            CaseId = caseId,
            Provider = provider,
            ProviderTargetScope = providerTargetScope,
            ProviderTargetId = EventReportGuards.NormalizeRequired(providerTargetId, MaxProviderTargetIdLength, nameof(providerTargetId)),
            SyncState = EventReportSyncState.Pending,
            CorrelationId = EventReportGuards.NormalizeRequired(correlationId, MaxCorrelationIdLength, nameof(correlationId)),
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
    }

    public void MarkSynced(
        string? providerCaseId,
        string? providerSignalId,
        string? providerUrl,
        DateTime utcNow)
    {
        ProviderCaseId = EventReportGuards.NormalizeOptional(providerCaseId, MaxProviderIdLength, nameof(providerCaseId));
        ProviderSignalId = EventReportGuards.NormalizeOptional(providerSignalId, MaxProviderIdLength, nameof(providerSignalId));
        ProviderUrl = EventReportGuards.NormalizeOptional(providerUrl, MaxProviderUrlLength, nameof(providerUrl));
        SyncState = EventReportSyncState.Synced;
        LastSyncedAt = utcNow;
        LastErrorCategory = null;
        UpdatedAt = utcNow;
    }

    public void MarkFailed(string errorCategory, DateTime utcNow)
    {
        SyncState = EventReportSyncState.Failed;
        LastErrorCategory = EventReportGuards.NormalizeRequired(errorCategory, MaxErrorCategoryLength, nameof(errorCategory));
        RetryCount++;
        UpdatedAt = utcNow;
    }

    public void Disable(DateTime utcNow)
    {
        SyncState = EventReportSyncState.Disabled;
        UpdatedAt = utcNow;
    }

    public void Ignore(DateTime utcNow)
    {
        SyncState = EventReportSyncState.Ignored;
        UpdatedAt = utcNow;
    }
}
