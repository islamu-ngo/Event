using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class EventContactShareExport : ITenantEntity, IAuditableEntity
{
    private readonly List<EventContactShareExportItem> _items = [];

    private EventContactShareExport()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; private set; }
    public Guid RecipientActorId { get; private set; }
    public Actor? RecipientActor { get; private set; }
    public Guid? EventId { get; private set; }
    public Event? Event { get; private set; }
    public Guid? ExportedByUserId { get; private set; }
    public User? ExportedByUser { get; private set; }
    public string Format { get; private set; } = string.Empty;
    public string PurposeCode { get; private set; } = string.Empty;
    public int StatusId { get; private set; }
    public string RequestedFieldKeysSnapshot { get; private set; } = string.Empty;
    public string IncludedFieldKeysSnapshot { get; private set; } = string.Empty;
    public string PolicyVersion { get; private set; } = string.Empty;
    public string? ContentHash { get; private set; }
    public int? FailureCategoryId { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? FailedAt { get; private set; }
    public int RowCount { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public IReadOnlyCollection<EventContactShareExportItem> Items => _items.AsReadOnly();

    public static EventContactShareExport Request(Guid tenantId, Guid recipientActorId, Guid? eventId, Guid? exportedByUserId,
        string format, string purposeCode, string requestedFieldKeysSnapshot, string policyVersion, DateTime requestedAt)
    {
        if (tenantId == Guid.Empty || recipientActorId == Guid.Empty || string.IsNullOrWhiteSpace(format) ||
            string.IsNullOrWhiteSpace(purposeCode) || string.IsNullOrWhiteSpace(requestedFieldKeysSnapshot) ||
            string.IsNullOrWhiteSpace(policyVersion) || requestedAt == default || requestedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Export request scope, purpose, fields, policy, and UTC time are required.");
        }

        return new EventContactShareExport
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            RecipientActorId = recipientActorId,
            EventId = eventId,
            ExportedByUserId = exportedByUserId,
            Format = format.Trim().ToLowerInvariant(),
            PurposeCode = purposeCode.Trim().ToUpperInvariant(),
            StatusId = (int)EventContactShareExportStatusEnum.Requested,
            RequestedFieldKeysSnapshot = requestedFieldKeysSnapshot.Trim(),
            IncludedFieldKeysSnapshot = "[]",
            PolicyVersion = policyVersion.Trim(),
            CreatedAt = requestedAt
        };
    }

    public void AddItem(EventContactShareExportItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.ExportId != Id)
        {
            throw new ArgumentException("Export item belongs to a different export.", nameof(item));
        }

        _items.Add(item);
    }

    public void Complete(string includedFieldKeysSnapshot, string contentHash, int rowCount, DateTime completedAt)
    {
        if (StatusId != (int)EventContactShareExportStatusEnum.Requested || rowCount < 0 || string.IsNullOrWhiteSpace(includedFieldKeysSnapshot) ||
            string.IsNullOrWhiteSpace(contentHash) || completedAt == default || completedAt.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("Only requested exports can complete with field snapshot, hash, row count, and UTC time.");
        }

        IncludedFieldKeysSnapshot = includedFieldKeysSnapshot.Trim();
        ContentHash = contentHash.Trim().ToLowerInvariant();
        RowCount = rowCount;
        StatusId = (int)EventContactShareExportStatusEnum.Completed;
        FailureCategoryId = (int)EventContactShareExportFailureCategoryEnum.None;
        CompletedAt = completedAt;
    }

    public void Fail(EventContactShareExportFailureCategoryEnum category, DateTime failedAt)
    {
        if (StatusId != (int)EventContactShareExportStatusEnum.Requested || category == EventContactShareExportFailureCategoryEnum.None ||
            !Enum.IsDefined(category) || failedAt == default || failedAt.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("Only requested exports can fail with a safe category and UTC time.");
        }

        StatusId = (int)EventContactShareExportStatusEnum.Failed;
        FailureCategoryId = (int)category;
        FailedAt = failedAt;
    }
}
