using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventReportEvidence : ITenantEntity, IAuditableEntity
{
    private const int MaxContentHashLength = 128;

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; private set; } = null!;
    public Guid ReportId { get; private set; }
    public EventReport Report { get; private set; } = null!;
    public EventReportEvidenceKind EvidenceKind { get; private set; }
    public string? TextBodyEncrypted { get; private set; }
    public Guid? StorageObjectId { get; private set; }
    public StorageObject? StorageObject { get; private set; }
    public string? ContentHash { get; private set; }
    public EventReportEvidenceClassification Classification { get; private set; }
    public DateTime? RetentionUntil { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public User? CreatedByUser { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static EventReportEvidence CreateReporterText(
        Guid tenantId,
        Guid reportId,
        string textBodyEncrypted,
        EventReportEvidenceClassification classification,
        DateTime? retentionUntil,
        Guid? createdByUserId,
        DateTime? createdAt = null)
    {
        EventReportGuards.RequireGuid(tenantId, nameof(tenantId), "Tenant id is required.");
        EventReportGuards.RequireGuid(reportId, nameof(reportId), "Report id is required.");
        EventReportGuards.RequireDefined(classification, nameof(classification));

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("Created-by user id cannot be empty.", nameof(createdByUserId));
        }

        return new EventReportEvidence
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ReportId = reportId,
            EvidenceKind = EventReportEvidenceKind.ReporterText,
            TextBodyEncrypted = EventReportGuards.NormalizeRequired(textBodyEncrypted, int.MaxValue, nameof(textBodyEncrypted)),
            Classification = classification,
            RetentionUntil = retentionUntil,
            CreatedByUserId = createdByUserId,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            CreatedBy = createdByUserId
        };
    }

    public static EventReportEvidence CreateSystemSignal(
        Guid tenantId,
        Guid reportId,
        string? contentHash,
        EventReportEvidenceClassification classification,
        DateTime? retentionUntil,
        DateTime? createdAt = null)
    {
        EventReportGuards.RequireGuid(tenantId, nameof(tenantId), "Tenant id is required.");
        EventReportGuards.RequireGuid(reportId, nameof(reportId), "Report id is required.");
        EventReportGuards.RequireDefined(classification, nameof(classification));

        return new EventReportEvidence
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ReportId = reportId,
            EvidenceKind = EventReportEvidenceKind.SystemSignal,
            ContentHash = EventReportGuards.NormalizeOptional(contentHash, MaxContentHashLength, nameof(contentHash)),
            Classification = classification,
            RetentionUntil = retentionUntil,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
    }
}
