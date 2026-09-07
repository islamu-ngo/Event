using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class OrganizerPaymentProviderAccountOperation : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    private const string ActiveUniquenessSlotValue = "active";

    private OrganizerPaymentProviderAccountOperation()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid OrganizerActorId { get; private set; }
    public string ProviderCode { get; private set; } = string.Empty;
    public string ConnectPlatformId { get; private set; } = string.Empty;
    public string ProviderIdempotencyKey { get; private set; } = string.Empty;
    public int StatusId { get; private set; }
    public string ActiveScopeKey { get; private set; } = string.Empty;
    public string ActiveUniquenessSlot { get; private set; } = string.Empty;
    public string? ExternalAccountId { get; private set; }
    public Guid? ConnectionId { get; private set; }
    public string? FailureCode { get; private set; }
    public string? ProviderRequestId { get; private set; }
    public string? ResolutionReason { get; private set; }
    public DateTime RequestedAt { get; private set; }
    public DateTime? ManualReconciliationRequiredAt { get; private set; }
    public DateTime? BoundAt { get; private set; }
    public DateTime? NoProviderAccountConfirmedAt { get; private set; }
    public DateTime? ProviderRejectedAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public bool IsUnresolved => StatusId is (int)OrganizerPaymentProviderAccountOperationStatus.ProviderCreateRequested
        or (int)OrganizerPaymentProviderAccountOperationStatus.ManualReconciliationRequired;

    public static OrganizerPaymentProviderAccountOperation CreateRequested(
        Guid id,
        Guid tenantId,
        Guid organizerActorId,
        string providerCode,
        string connectPlatformId,
        DateTime requestedAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || organizerActorId == Guid.Empty)
        {
            throw new ArgumentException("Operation identities are required.");
        }

        string normalizedProvider = OrganizerPaymentProviderConnection.NormalizeProviderCode(providerCode);
        string normalizedPlatform = OrganizerPaymentProviderConnection.NormalizeProviderIdentity(connectPlatformId, nameof(connectPlatformId), 120, preserveCase: false);
        DateTime timestamp = OrganizerPaymentProviderConnection.EnsureUtc(requestedAt, nameof(requestedAt));
        return new OrganizerPaymentProviderAccountOperation
        {
            Id = id,
            TenantId = tenantId,
            OrganizerActorId = organizerActorId,
            ProviderCode = normalizedProvider,
            ConnectPlatformId = normalizedPlatform,
            ProviderIdempotencyKey = $"organizer-payment-account-{id:N}",
            StatusId = (int)OrganizerPaymentProviderAccountOperationStatus.ProviderCreateRequested,
            ActiveScopeKey = CreateActiveScopeKey(tenantId, organizerActorId, normalizedProvider, normalizedPlatform),
            ActiveUniquenessSlot = ActiveUniquenessSlotValue,
            RequestedAt = timestamp,
            CreatedAt = timestamp,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    public void MarkManualReconciliationRequired(string failureCode, string? providerRequestId, DateTime occurredAt)
    {
        EnsureUnresolved();
        FailureCode = NormalizeOptionalCode(failureCode, nameof(failureCode), 120) ?? "organizer_payment_provider_manual_reconciliation_required";
        ProviderRequestId = NormalizeOptionalCode(providerRequestId, nameof(providerRequestId), 120);
        ManualReconciliationRequiredAt = OrganizerPaymentProviderConnection.EnsureUtc(occurredAt, nameof(occurredAt));
        StatusId = (int)OrganizerPaymentProviderAccountOperationStatus.ManualReconciliationRequired;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void BindToConnection(Guid connectionId, string externalAccountId, DateTime boundAt)
    {
        EnsureUnresolved();
        if (connectionId == Guid.Empty)
        {
            throw new ArgumentException("Connection identity is required.", nameof(connectionId));
        }

        ConnectionId = connectionId;
        ExternalAccountId = OrganizerPaymentProviderConnection.NormalizeProviderIdentity(externalAccountId, nameof(externalAccountId), 200, preserveCase: true);
        BoundAt = OrganizerPaymentProviderConnection.EnsureUtc(boundAt, nameof(boundAt));
        StatusId = (int)OrganizerPaymentProviderAccountOperationStatus.BoundToConnection;
        ActiveUniquenessSlot = CreateTerminalUniquenessSlot(nameof(OrganizerPaymentProviderAccountOperationStatus.BoundToConnection));
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void RejectByProvider(string failureCode, string? providerRequestId, DateTime rejectedAt)
    {
        EnsureUnresolved();
        FailureCode = NormalizeOptionalCode(failureCode, nameof(failureCode), 120) ?? "organizer_payment_provider_rejected";
        ProviderRequestId = NormalizeOptionalCode(providerRequestId, nameof(providerRequestId), 120);
        ProviderRejectedAt = OrganizerPaymentProviderConnection.EnsureUtc(rejectedAt, nameof(rejectedAt));
        StatusId = (int)OrganizerPaymentProviderAccountOperationStatus.ProviderRejected;
        ActiveUniquenessSlot = CreateTerminalUniquenessSlot(nameof(OrganizerPaymentProviderAccountOperationStatus.ProviderRejected));
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void ConfirmNoProviderAccount(string reason, DateTime confirmedAt)
    {
        EnsureUnresolved();
        ResolutionReason = NormalizeOptionalCode(reason, nameof(reason), 160) ?? throw new ArgumentException("Resolution reason is required.", nameof(reason));
        NoProviderAccountConfirmedAt = OrganizerPaymentProviderConnection.EnsureUtc(confirmedAt, nameof(confirmedAt));
        StatusId = (int)OrganizerPaymentProviderAccountOperationStatus.NoProviderAccountConfirmed;
        ActiveUniquenessSlot = CreateTerminalUniquenessSlot(nameof(OrganizerPaymentProviderAccountOperationStatus.NoProviderAccountConfirmed));
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    internal static string CreateActiveScopeKey(Guid tenantId, Guid organizerActorId, string providerCode, string connectPlatformId) =>
        string.Join('|', tenantId.ToString("N"), organizerActorId.ToString("N"), providerCode, connectPlatformId);

    private string CreateTerminalUniquenessSlot(string statusName) => $"{statusName.ToLowerInvariant()}:{Id:N}";

    private void EnsureUnresolved()
    {
        if (!IsUnresolved)
        {
            throw new InvalidOperationException("Terminal organizer payment account operations cannot be reactivated.");
        }
    }

    private static string? NormalizeOptionalCode(string? value, string parameterName, int maxLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return null;
        }

        if (normalized.Length > maxLength || normalized.Any(char.IsControl))
        {
            throw new ArgumentException($"Value must be bounded to {maxLength} characters.", parameterName);
        }

        return normalized;
    }
}
