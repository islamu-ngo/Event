using Explore.Domain.Interfaces;

namespace Explore.Domain;

public enum TicketPurchaseAccessMode
{
    AuthenticatedAccount = 1,
    VerifiedContact = 2,
    NameOnly = 3,
}

public sealed class TicketPurchasePolicyVersion : ITenantEntity, IConcurrencyAware
{
    private TicketPurchasePolicyVersion(
        Guid id,
        Guid tenantId,
        Guid eventId,
        Guid instancePolicyVersionId,
        Guid tenantPolicyVersionId,
        Guid eventPolicyVersionId,
        int instanceCeiling,
        int tenantCeiling,
        int eventCeiling,
        DateTime createdAt)
    {
        Id = id;
        TenantId = tenantId;
        EventId = eventId;
        InstancePolicyVersionId = instancePolicyVersionId;
        TenantPolicyVersionId = tenantPolicyVersionId;
        EventPolicyVersionId = eventPolicyVersionId;
        InstanceCeiling = instanceCeiling;
        TenantCeiling = tenantCeiling;
        EventCeiling = eventCeiling;
        EffectiveCeiling = Math.Min(
            instanceCeiling,
            Math.Min(tenantCeiling, eventCeiling));
        CreatedAt = createdAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private TicketPurchasePolicyVersion()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid InstancePolicyVersionId { get; private set; }
    public Guid TenantPolicyVersionId { get; private set; }
    public Guid EventPolicyVersionId { get; private set; }
    public int InstanceCeiling { get; private set; }
    public int TenantCeiling { get; private set; }
    public int EventCeiling { get; private set; }
    public int EffectiveCeiling { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid ConcurrencyStamp { get; set; }

    public static TicketPurchasePolicyVersion Create(
        Guid tenantId,
        Guid eventId,
        Guid instancePolicyVersionId,
        Guid tenantPolicyVersionId,
        Guid eventPolicyVersionId,
        int instanceCeiling,
        int tenantCeiling,
        int eventCeiling,
        DateTime createdAt)
    {
        if (tenantId == Guid.Empty
            || eventId == Guid.Empty
            || instancePolicyVersionId == Guid.Empty
            || tenantPolicyVersionId == Guid.Empty
            || eventPolicyVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Purchase policy identities must be non-empty.");
        }

        if (instanceCeiling <= 0 || tenantCeiling <= 0 || eventCeiling <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(instanceCeiling),
                "Purchase ceilings must be positive.");
        }

        if (createdAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Purchase policy timestamps must be UTC.",
                nameof(createdAt));
        }

        return new TicketPurchasePolicyVersion(
            Guid.CreateVersion7(),
            tenantId,
            eventId,
            instancePolicyVersionId,
            tenantPolicyVersionId,
            eventPolicyVersionId,
            instanceCeiling,
            tenantCeiling,
            eventCeiling,
            createdAt);
    }
}

public sealed record TicketPurchaseAuthorityDimension
{
    private TicketPurchaseAuthorityDimension(
        TicketPurchaseAccessMode accessMode,
        string enforcementKey,
        Guid? actingAccountUserId,
        Guid? purchaserActorId,
        Guid? orderId,
        bool supportsHardCrossOrderCeiling)
    {
        AccessMode = accessMode;
        EnforcementKey = enforcementKey;
        ActingAccountUserId = actingAccountUserId;
        PurchaserActorId = purchaserActorId;
        OrderId = orderId;
        SupportsHardCrossOrderCeiling = supportsHardCrossOrderCeiling;
    }

    public TicketPurchaseAccessMode AccessMode { get; }
    public string EnforcementKey { get; }
    public Guid? ActingAccountUserId { get; }
    public Guid? PurchaserActorId { get; }
    public Guid? OrderId { get; }
    public bool SupportsHardCrossOrderCeiling { get; }

    public static TicketPurchaseAuthorityDimension Authenticated(
        Guid actingAccountUserId,
        Guid? purchaserActorId)
    {
        if (actingAccountUserId == Guid.Empty
            || purchaserActorId == Guid.Empty)
        {
            throw new ArgumentException(
                "Authenticated purchase authority identities must be non-empty.");
        }

        return new TicketPurchaseAuthorityDimension(
            TicketPurchaseAccessMode.AuthenticatedAccount,
            $"account:{actingAccountUserId:N}",
            actingAccountUserId,
            purchaserActorId,
            null,
            supportsHardCrossOrderCeiling: true);
    }

    public static TicketPurchaseAuthorityDimension VerifiedContact(
        string normalizedContactHash)
    {
        string hash = RegistrationSha256Hash.Normalize(
            normalizedContactHash,
            nameof(normalizedContactHash),
            "Verified contact hash");
        return new TicketPurchaseAuthorityDimension(
            TicketPurchaseAccessMode.VerifiedContact,
            $"contact:{hash}",
            null,
            null,
            null,
            supportsHardCrossOrderCeiling: true);
    }

    public static TicketPurchaseAuthorityDimension NameOnly(Guid orderId)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException(
                "Name-only purchase authority requires an order identity.",
                nameof(orderId));
        }

        return new TicketPurchaseAuthorityDimension(
            TicketPurchaseAccessMode.NameOnly,
            $"order:{orderId:N}",
            null,
            null,
            orderId,
            supportsHardCrossOrderCeiling: false);
    }
}

public sealed record TicketPurchaseOperationIdentity
{
    private TicketPurchaseOperationIdentity(
        string keyHash,
        string fingerprintHash)
    {
        KeyHash = keyHash;
        FingerprintHash = fingerprintHash;
    }

    public string KeyHash { get; }
    public string FingerprintHash { get; }

    public static TicketPurchaseOperationIdentity Create(
        string keyHash,
        string fingerprintHash) => new(
        RegistrationSha256Hash.Normalize(
            keyHash,
            nameof(keyHash),
            "Purchase operation key hash"),
        RegistrationSha256Hash.Normalize(
            fingerprintHash,
            nameof(fingerprintHash),
            "Purchase operation fingerprint hash"));
}

public sealed record TicketPurchaseReservationRequest
{
    public TicketPurchaseReservationRequest(
        Guid tenantId,
        Guid eventId,
        Guid orderId,
        int quantity,
        TicketPurchaseAuthorityDimension authority,
        TicketPurchaseOperationIdentity operation)
    {
        if (tenantId == Guid.Empty
            || eventId == Guid.Empty
            || orderId == Guid.Empty)
        {
            throw new ArgumentException(
                "Purchase reservation identities must be non-empty.");
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity));
        }

        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(operation);
        TenantId = tenantId;
        EventId = eventId;
        OrderId = orderId;
        Quantity = quantity;
        Authority = authority;
        Operation = operation;
    }

    public Guid TenantId { get; init; }
    public Guid EventId { get; init; }
    public Guid OrderId { get; init; }
    public int Quantity { get; init; }
    public TicketPurchaseAuthorityDimension Authority { get; init; }
    public TicketPurchaseOperationIdentity Operation { get; init; }
}

public sealed class TicketPurchaseAuthorityUsage :
    ITenantEntity,
    IConcurrencyAware
{
    private TicketPurchaseAuthorityUsage(
        Guid tenantId,
        Guid eventId,
        TicketPurchaseAuthorityDimension authority,
        DateTime createdAt)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        EventId = eventId;
        AccessMode = authority.AccessMode;
        EnforcementKey = authority.EnforcementKey;
        ActingAccountUserId = authority.ActingAccountUserId;
        PurchaserActorId = authority.PurchaserActorId;
        OrderId = authority.OrderId;
        SupportsHardCrossOrderCeiling =
            authority.SupportsHardCrossOrderCeiling;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private TicketPurchaseAuthorityUsage()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public TicketPurchaseAccessMode AccessMode { get; private set; }
    public string EnforcementKey { get; private set; } = string.Empty;
    public Guid? ActingAccountUserId { get; private set; }
    public Guid? PurchaserActorId { get; private set; }
    public Guid? OrderId { get; private set; }
    public bool SupportsHardCrossOrderCeiling { get; private set; }
    public int ConsumedQuantity { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public Guid ConcurrencyStamp { get; set; }

    public static TicketPurchaseAuthorityUsage Create(
        Guid tenantId,
        Guid eventId,
        TicketPurchaseAuthorityDimension authority,
        DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (tenantId == Guid.Empty || eventId == Guid.Empty)
        {
            throw new ArgumentException(
                "Purchase authority usage identities must be non-empty.");
        }

        if (createdAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Purchase authority usage timestamps must be UTC.",
                nameof(createdAt));
        }

        return new TicketPurchaseAuthorityUsage(
            tenantId,
            eventId,
            authority,
            createdAt);
    }

    public bool TryConsume(
        int quantity,
        int effectiveCeiling,
        DateTime timestamp)
    {
        if (quantity <= 0 || effectiveCeiling <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity));
        }

        if (timestamp.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Purchase authority usage timestamps must be UTC.",
                nameof(timestamp));
        }

        if (ConsumedQuantity > effectiveCeiling - quantity)
        {
            return false;
        }

        ConsumedQuantity = checked(ConsumedQuantity + quantity);
        UpdatedAt = timestamp;
        return true;
    }
}

public enum TicketPurchaseReservationDisposition
{
    Reserved = 1,
    Replay = 2,
    CeilingExceeded = 3,
    OperationConflict = 4,
    Unavailable = 5,
}

public sealed class TicketPurchaseOperation :
    ITenantEntity,
    IConcurrencyAware
{
    private TicketPurchaseOperation(
        Guid tenantId,
        Guid eventId,
        Guid orderId,
        Guid policyVersionId,
        Guid? authorityUsageId,
        TicketPurchaseOperationIdentity identity,
        TicketPurchaseReservationDisposition disposition,
        int requestedQuantity,
        int effectiveCeiling,
        int consumedQuantity,
        DateTime createdAt)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        EventId = eventId;
        OrderId = orderId;
        PolicyVersionId = policyVersionId;
        AuthorityUsageId = authorityUsageId;
        KeyHash = identity.KeyHash;
        FingerprintHash = identity.FingerprintHash;
        Disposition = disposition;
        RequestedQuantity = requestedQuantity;
        EffectiveCeiling = effectiveCeiling;
        ConsumedQuantity = consumedQuantity;
        CreatedAt = createdAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private TicketPurchaseOperation()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid PolicyVersionId { get; private set; }
    public Guid? AuthorityUsageId { get; private set; }
    public string KeyHash { get; private set; } = string.Empty;
    public string FingerprintHash { get; private set; } = string.Empty;
    public TicketPurchaseReservationDisposition Disposition { get; private set; }
    public int RequestedQuantity { get; private set; }
    public int EffectiveCeiling { get; private set; }
    public int ConsumedQuantity { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid ConcurrencyStamp { get; set; }

    public static TicketPurchaseOperation Record(
        TicketPurchasePolicyVersion policy,
        TicketPurchaseReservationRequest request,
        Guid? authorityUsageId,
        TicketPurchaseReservationDisposition disposition,
        int consumedQuantity,
        DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(request);
        if (disposition is not (
            TicketPurchaseReservationDisposition.Reserved
            or TicketPurchaseReservationDisposition.CeilingExceeded))
        {
            throw new ArgumentOutOfRangeException(
                nameof(disposition));
        }

        return new TicketPurchaseOperation(
            request.TenantId,
            request.EventId,
            request.OrderId,
            policy.Id,
            authorityUsageId,
            request.Operation,
            disposition,
            request.Quantity,
            policy.EffectiveCeiling,
            consumedQuantity,
            createdAt);
    }

    public TicketPurchaseReservationResult ToReplayResult() => new(
        Disposition == TicketPurchaseReservationDisposition.Reserved
            ? TicketPurchaseReservationDisposition.Replay
            : Disposition,
        Disposition == TicketPurchaseReservationDisposition.Reserved
            ? Id
            : null,
        EffectiveCeiling,
        ConsumedQuantity);

    public TicketPurchaseReservationResult ToInitialResult() => new(
        Disposition,
        Disposition == TicketPurchaseReservationDisposition.Reserved
            ? Id
            : null,
        EffectiveCeiling,
        ConsumedQuantity);
}

public sealed record TicketPurchaseReservationResult(
    TicketPurchaseReservationDisposition Disposition,
    Guid? ReservationId,
    int EffectiveCeiling,
    int ConsumedQuantity);
