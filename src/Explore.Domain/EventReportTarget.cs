using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventReportTarget : ITenantEntity
{
    private const int MaxFieldPathLength = 200;

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; private set; } = null!;
    public Guid ReportId { get; private set; }
    public EventReport Report { get; private set; } = null!;
    public EventReportTargetKind TargetKind { get; private set; }
    public Guid TargetId { get; private set; }
    public string? FieldPath { get; private set; }
    public Guid? StorageObjectId { get; private set; }
    public StorageObject? StorageObject { get; private set; }

    public static EventReportTarget CreateEventTarget(Guid tenantId, Guid reportId, Guid eventId)
    {
        return Create(tenantId, reportId, EventReportTargetKind.Event, eventId, fieldPath: null, storageObjectId: null);
    }

    public static EventReportTarget Create(
        Guid tenantId,
        Guid reportId,
        EventReportTargetKind targetKind,
        Guid targetId,
        string? fieldPath,
        Guid? storageObjectId)
    {
        EventReportGuards.RequireGuid(tenantId, nameof(tenantId), "Tenant id is required.");
        EventReportGuards.RequireGuid(reportId, nameof(reportId), "Report id is required.");
        EventReportGuards.RequireGuid(targetId, nameof(targetId), "Target id is required.");
        EventReportGuards.RequireDefined(targetKind, nameof(targetKind));

        if (storageObjectId == Guid.Empty)
        {
            throw new ArgumentException("Storage object id cannot be empty.", nameof(storageObjectId));
        }

        return new EventReportTarget
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ReportId = reportId,
            TargetKind = targetKind,
            TargetId = targetId,
            FieldPath = EventReportGuards.NormalizeOptional(fieldPath, MaxFieldPathLength, nameof(fieldPath)),
            StorageObjectId = storageObjectId
        };
    }
}
