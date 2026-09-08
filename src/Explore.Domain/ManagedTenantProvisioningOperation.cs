using Explore.Domain.Interfaces;

namespace Explore.Domain;

public enum ManagedTenantProvisioningStatus
{
    Pending = 1,
    Processing = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5
}

public sealed class ManagedTenantProvisioningOperation : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid ManagedInstanceId { get; set; }
    public required string ExternalRequestId { get; set; }
    public required string ExternalCustomerReference { get; set; }
    public required string RequestHash { get; set; }
    public string? RequestJson { get; set; }
    public required string TenantSlug { get; set; }
    public Guid CurrentOutboxMessageId { get; set; }
    public string? CorrelationId { get; set; }
    public ManagedTenantProvisioningStatus Status { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? TenantAdministratorUserId { get; set; }
    public string? FailureCode { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public uint RowVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool CanCancel => Status == ManagedTenantProvisioningStatus.Pending;

    public void Start(DateTime startedAt)
    {
        if (Status == ManagedTenantProvisioningStatus.Processing)
        {
            return;
        }

        if (Status != ManagedTenantProvisioningStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending tenant provisioning operation can start.");
        }

        StartedAt = EnsureUtc(startedAt, nameof(startedAt));
        Status = ManagedTenantProvisioningStatus.Processing;
        UpdatedAt = StartedAt;
    }

    public void Complete(Guid tenantId, Guid tenantAdministratorUserId, DateTime completedAt)
    {
        if (Status == ManagedTenantProvisioningStatus.Succeeded)
        {
            return;
        }

        if (Status != ManagedTenantProvisioningStatus.Processing)
        {
            throw new InvalidOperationException("Only a processing tenant provisioning operation can complete.");
        }

        if (tenantId == Guid.Empty || tenantAdministratorUserId == Guid.Empty)
        {
            throw new ArgumentException("Provisioning result identifiers must be non-empty.");
        }

        TenantId = tenantId;
        TenantAdministratorUserId = tenantAdministratorUserId;
        CompletedAt = EnsureUtc(completedAt, nameof(completedAt));
        FailureCode = null;
        RequestJson = null;
        Status = ManagedTenantProvisioningStatus.Succeeded;
        UpdatedAt = CompletedAt;
    }

    public void Fail(string failureCode, DateTime failedAt)
    {
        if (Status is not (ManagedTenantProvisioningStatus.Pending or ManagedTenantProvisioningStatus.Processing))
        {
            throw new InvalidOperationException("Only a pending or processing tenant provisioning operation can fail.");
        }

        FailureCode = Require(failureCode, 100, nameof(failureCode));
        FailedAt = EnsureUtc(failedAt, nameof(failedAt));
        RequestJson = null;
        Status = ManagedTenantProvisioningStatus.Failed;
        UpdatedAt = FailedAt;
    }

    public void Cancel(DateTime cancelledAt)
    {
        if (Status == ManagedTenantProvisioningStatus.Cancelled)
        {
            return;
        }

        if (Status != ManagedTenantProvisioningStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending tenant provisioning operation can be cancelled.");
        }

        CancelledAt = EnsureUtc(cancelledAt, nameof(cancelledAt));
        RequestJson = null;
        Status = ManagedTenantProvisioningStatus.Cancelled;
        UpdatedAt = CancelledAt;
    }

    private static string Require(string value, int maximumLength, string parameterName)
    {
        string? normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximumLength)
        {
            throw new ArgumentException("A bounded non-empty value is required.", parameterName);
        }

        return normalized;
    }

    private static DateTime EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }

        return value;
    }
}
