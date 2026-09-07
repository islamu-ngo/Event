using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class AdmissionRecoveryRequestIntent : ITenantEntity, IConcurrencyAware
{
    private AdmissionRecoveryRequestIntent()
    {
    }

    public AdmissionRecoveryRequestIntent(
        Guid id,
        Guid tenantId,
        string protectedIdentity,
        int protectionVersion,
        DateTime createdAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty ||
            string.IsNullOrWhiteSpace(protectedIdentity) ||
            protectionVersion < 1 || createdAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Complete protected recovery request intent is required.");
        }

        Id = id;
        TenantId = tenantId;
        ProtectedIdentity = protectedIdentity;
        ProtectionVersion = protectionVersion;
        CreatedAt = createdAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public string ProtectedIdentity { get; private set; } = string.Empty;
    public int ProtectionVersion { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public Guid ConcurrencyStamp { get; set; }

    public void Complete(DateTime processedAtUtc)
    {
        if (processedAtUtc.Kind != DateTimeKind.Utc || processedAtUtc < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(processedAtUtc));
        }

        ProcessedAt ??= processedAtUtc;
        ProtectedIdentity = string.Empty;
    }

    public override string ToString() =>
        $"AdmissionRecoveryRequestIntent({Id}, processed={ProcessedAt.HasValue}, <redacted>)";
}

public sealed class AdmissionRecoveryDeliveryIntent : ITenantEntity, IConcurrencyAware
{
    private AdmissionRecoveryDeliveryIntent()
    {
    }

    public AdmissionRecoveryDeliveryIntent(
        Guid id,
        Guid tenantId,
        Guid recoveryRequestId,
        Guid admissionTicketId,
        string purpose,
        int capabilityVersion,
        string protectedMaterial,
        int protectionVersion,
        DateTime createdAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || recoveryRequestId == Guid.Empty ||
            admissionTicketId == Guid.Empty || string.IsNullOrWhiteSpace(purpose) ||
            capabilityVersion < 1 || string.IsNullOrWhiteSpace(protectedMaterial) ||
            protectionVersion < 1 || createdAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Complete protected recovery delivery lineage is required.");
        }

        Id = id;
        TenantId = tenantId;
        RecoveryRequestId = recoveryRequestId;
        AdmissionTicketId = admissionTicketId;
        Purpose = purpose;
        CapabilityVersion = capabilityVersion;
        ProtectedMaterial = protectedMaterial;
        ProtectionVersion = protectionVersion;
        CreatedAt = createdAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid RecoveryRequestId { get; private set; }
    public Guid AdmissionTicketId { get; private set; }
    public string Purpose { get; private set; } = string.Empty;
    public int CapabilityVersion { get; private set; }
    public string ProtectedMaterial { get; private set; } = string.Empty;
    public int ProtectionVersion { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? RoutedAt { get; private set; }
    public DateTime? HandoffCompletedAt { get; private set; }
    public string? HandoffReceiptId { get; private set; }
    public Guid ConcurrencyStamp { get; set; }

    public void MarkRouted(DateTime routedAtUtc)
    {
        if (routedAtUtc.Kind != DateTimeKind.Utc || routedAtUtc < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(routedAtUtc));
        }

        RoutedAt ??= routedAtUtc;
    }

    public void CompleteHandoff(string receiptId, DateTime completedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(receiptId) || completedAtUtc.Kind != DateTimeKind.Utc ||
            completedAtUtc < CreatedAt || RoutedAt is null)
        {
            throw new InvalidOperationException("Recovery delivery requires a routed receipt-bearing handoff.");
        }

        HandoffCompletedAt ??= completedAtUtc;
        HandoffReceiptId ??= receiptId.Trim();
        ProtectedMaterial = string.Empty;
    }

    public override string ToString() =>
        $"AdmissionRecoveryDeliveryIntent({Id}, request={RecoveryRequestId}, version={CapabilityVersion}, <redacted>)";
}
