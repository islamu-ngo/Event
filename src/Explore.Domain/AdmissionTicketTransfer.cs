using System.Security.Cryptography;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public enum AdmissionTicketTransferStatus
{
    Offered = 1,
    Accepted = 2,
    Cancelled = 3,
    Expired = 4,
}

public enum AdmissionTicketTransferOutcome
{
    Offered = 1,
    AlreadyOffered = 2,
    Accepted = 3,
    StaleGeneration = 4,
    Expired = 5,
    HopLimitReached = 6,
    AlreadyCheckedIn = 7,
    NotTransferable = 8,
    Unavailable = 9,
    Cancelled = 10,
}

public sealed class TicketTransferPolicy :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private Guid _tenantId;

    private TicketTransferPolicy()
    {
    }

    private TicketTransferPolicy(
        Guid id,
        Guid tenantId,
        Guid ticketCatalogVersionId,
        Guid eventTicketTypeId,
        bool isEnabled,
        int maximumHops,
        int offerLifetimeMinutes,
        int cutoffMinutesBeforeEvent,
        DateTime createdAt)
    {
        Id = id;
        TenantId = tenantId;
        TicketCatalogVersionId = ticketCatalogVersionId;
        EventTicketTypeId = eventTicketTypeId;
        IsEnabled = isEnabled;
        MaximumHops = maximumHops;
        OfferLifetimeMinutes = offerLifetimeMinutes;
        CutoffMinutesBeforeEvent = cutoffMinutesBeforeEvent;
        CreatedAt = createdAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(
            ref _tenantId,
            value,
            nameof(TicketTransferPolicy));
    }

    public Guid TicketCatalogVersionId { get; private set; }
    public Guid EventTicketTypeId { get; private set; }
    public bool IsEnabled { get; private set; }
    public int MaximumHops { get; private set; }
    public int OfferLifetimeMinutes { get; private set; }
    public int CutoffMinutesBeforeEvent { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static TicketTransferPolicy Create(
        Guid id,
        Guid tenantId,
        EventTicketType ticketType,
        bool isEnabled,
        int maximumHops,
        int offerLifetimeMinutes,
        int cutoffMinutesBeforeEvent,
        DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(ticketType);
        RequireUuidV7(id, nameof(id));
        DateTime created = EnsureUtc(createdAt, nameof(createdAt));
        if (tenantId == Guid.Empty
            || ticketType.TenantId != tenantId)
        {
            throw new ArgumentException(
                "Transfer policy must match its ticket type.");
        }
        if (maximumHops is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumHops));
        }
        if (offerLifetimeMinutes is < 5 or > 43_200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offerLifetimeMinutes));
        }
        if (cutoffMinutesBeforeEvent is < 0 or > 525_600)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cutoffMinutesBeforeEvent));
        }

        return new TicketTransferPolicy(
            id,
            tenantId,
            ticketType.CatalogId,
            ticketType.Id,
            isEnabled,
            maximumHops,
            offerLifetimeMinutes,
            cutoffMinutesBeforeEvent,
            created);
    }

    public DateTime? GetOfferExpiry(
        int completedHops,
        DateTime eventStartsAtUtc,
        DateTime offeredAtUtc)
    {
        DateTime startsAt = EnsureUtc(
            eventStartsAtUtc,
            nameof(eventStartsAtUtc));
        DateTime offeredAt = EnsureUtc(
            offeredAtUtc,
            nameof(offeredAtUtc));
        if (!IsEnabled
            || completedHops < 0
            || completedHops >= MaximumHops)
        {
            return null;
        }

        DateTime cutoff = startsAt.AddMinutes(
            -CutoffMinutesBeforeEvent);
        if (offeredAt >= cutoff)
        {
            return null;
        }

        DateTime lifetimeExpiry = offeredAt.AddMinutes(
            OfferLifetimeMinutes);
        return lifetimeExpiry <= cutoff
            ? lifetimeExpiry
            : cutoff;
    }

    private static void RequireUuidV7(
        Guid value,
        string parameterName)
    {
        if (value == Guid.Empty
            || value.Version != 7)
        {
            throw new ArgumentException(
                "Identity must be UUIDv7.",
                parameterName);
        }
    }

    private static DateTime EnsureUtc(
        DateTime value,
        string parameterName)
    {
        if (value == default
            || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Timestamp must be UTC.",
                parameterName);
        }

        return value;
    }
}

public sealed class AdmissionTicketTransfer :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private Guid _tenantId;

    private AdmissionTicketTransfer()
    {
    }

    private AdmissionTicketTransfer(
        Guid id,
        AdmissionTicket ticket,
        Guid offerOperationKey,
        string capabilityDigest,
        int transferHop,
        int credentialGeneration,
        DateTime expiresAt,
        DateTime offeredAt)
    {
        Id = id;
        TenantId = ticket.TenantId;
        EventId = ticket.EventId;
        AdmissionTicketId = ticket.Id;
        OpenAdmissionTicketId = ticket.Id;
        RegistrationOrderId = ticket.RegistrationOrderId;
        RegistrationOrderLineId =
            ticket.RegistrationOrderLineId;
        RegistrationTicketAssignmentId =
            ticket.RegistrationTicketAssignmentId;
        FromParticipantId = ticket.ParticipantId;
        OfferOperationKey = offerOperationKey;
        CapabilityDigest = capabilityDigest;
        TransferHop = transferHop;
        CredentialGeneration = credentialGeneration;
        StatusId = (int)AdmissionTicketTransferStatus.Offered;
        ExpiresAt = expiresAt;
        OfferedAt = offeredAt;
        CreatedAt = offeredAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(
            ref _tenantId,
            value,
            nameof(AdmissionTicketTransfer));
    }

    public Guid EventId { get; private set; }
    public Guid AdmissionTicketId { get; private set; }
    public Guid OpenAdmissionTicketId { get; private set; }
    public Guid RegistrationOrderId { get; private set; }
    public Guid RegistrationOrderLineId { get; private set; }
    public Guid RegistrationTicketAssignmentId { get; private set; }
    public Guid FromParticipantId { get; private set; }
    public Guid? ToParticipantId { get; private set; }
    public Guid? RecipientSubjectUserId { get; private set; }
    public Guid OfferOperationKey { get; private set; }
    public string CapabilityDigest { get; private set; } =
        string.Empty;
    public int TransferHop { get; private set; }
    public int CredentialGeneration { get; private set; }
    public int? AcceptedCredentialGeneration { get; private set; }
    public int StatusId { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime OfferedAt { get; private set; }
    public DateTime? CapabilityConsumedAt { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public DateTime? ExpiredAt { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool IsOpen =>
        StatusId == (int)AdmissionTicketTransferStatus.Offered;

    public static AdmissionTicketTransfer Offer(
        Guid id,
        AdmissionTicket ticket,
        TicketTransferPolicy policy,
        Guid offerOperationKey,
        string capabilityDigest,
        DateTime eventStartsAtUtc,
        DateTime offeredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(policy);
        RequireUuidV7(id, nameof(id));
        RequireUuidV7(
            offerOperationKey,
            nameof(offerOperationKey));
        if (ticket.TenantId != policy.TenantId
            || ticket.TicketCatalogVersionId !=
            policy.TicketCatalogVersionId
            || ticket.EventTicketTypeId !=
            policy.EventTicketTypeId)
        {
            throw new ArgumentException(
                "Transfer policy does not govern this ticket.");
        }
        if (!ticket.IsActive)
        {
            throw new InvalidOperationException(
                "Only an active admission ticket can transfer.");
        }

        DateTime offeredAt = EnsureUtc(
            offeredAtUtc,
            nameof(offeredAtUtc));
        DateTime? expiresAt = policy.GetOfferExpiry(
            ticket.TransferHopCount,
            eventStartsAtUtc,
            offeredAt);
        if (!expiresAt.HasValue)
        {
            throw new InvalidOperationException(
                "Transfer policy does not permit a new offer.");
        }

        return new AdmissionTicketTransfer(
            id,
            ticket,
            offerOperationKey,
            NormalizeDigest(capabilityDigest),
            ticket.TransferHopCount + 1,
            ticket.CredentialGeneration,
            expiresAt.Value,
            offeredAt);
    }

    public bool MatchesCapability(string capabilityDigest)
    {
        if (!IsOpen
            || !TryDecodeDigest(
                capabilityDigest,
                out byte[] candidate))
        {
            return false;
        }

        byte[] stored = Convert.FromBase64String(
            CapabilityDigest);
        return CryptographicOperations.FixedTimeEquals(
            stored,
            candidate);
    }

    internal void EnsureCanAccept(
        AdmissionTicket ticket,
        RegistrationParticipant recipient,
        Guid recipientSubjectUserId,
        int acceptedCredentialGeneration,
        DateTime acceptedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(recipient);
        DateTime acceptedAt = EnsureUtc(
            acceptedAtUtc,
            nameof(acceptedAtUtc));
        if (!IsOpen
            || acceptedAt > ExpiresAt
            || ticket.Id != AdmissionTicketId
            || ticket.TenantId != TenantId
            || ticket.ParticipantId != FromParticipantId
            || ticket.CredentialGeneration !=
            CredentialGeneration
            || acceptedCredentialGeneration !=
            CredentialGeneration + 1
            || recipient.TenantId != TenantId
            || recipient.RegistrationOrderId !=
            RegistrationOrderId
            || recipient.Id == FromParticipantId
            || recipient.LinkedUserId !=
            recipientSubjectUserId
            || recipientSubjectUserId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Transfer acceptance authority is stale or inconsistent.");
        }
    }

    public void Accept(
        AdmissionTicket ticket,
        RegistrationParticipant recipient,
        Guid recipientSubjectUserId,
        int acceptedCredentialGeneration,
        DateTime acceptedAtUtc)
    {
        DateTime acceptedAt = EnsureUtc(
            acceptedAtUtc,
            nameof(acceptedAtUtc));
        if (!IsOpen
            || acceptedAt > ExpiresAt
            || ticket.Id != AdmissionTicketId
            || ticket.ParticipantId != FromParticipantId
            || ticket.CredentialGeneration !=
            acceptedCredentialGeneration
            || recipient.LinkedUserId !=
            recipientSubjectUserId)
        {
            throw new InvalidOperationException(
                "Rotated credential does not match transfer acceptance.");
        }
        ToParticipantId = recipient.Id;
        RecipientSubjectUserId = recipientSubjectUserId;
        AcceptedCredentialGeneration =
            acceptedCredentialGeneration;
        CapabilityConsumedAt = acceptedAt;
        AcceptedAt = acceptedAt;
        StatusId =
            (int)AdmissionTicketTransferStatus.Accepted;
        Close(acceptedAt);
    }

    public void Cancel(DateTime cancelledAtUtc)
    {
        DateTime cancelledAt = EnsureUtc(
            cancelledAtUtc,
            nameof(cancelledAtUtc));
        if (!IsOpen)
        {
            return;
        }

        CancelledAt = cancelledAt;
        StatusId =
            (int)AdmissionTicketTransferStatus.Cancelled;
        Close(cancelledAt);
    }

    public void Expire(DateTime expiredAtUtc)
    {
        DateTime expiredAt = EnsureUtc(
            expiredAtUtc,
            nameof(expiredAtUtc));
        if (!IsOpen
            || expiredAt < ExpiresAt)
        {
            return;
        }

        ExpiredAt = expiredAt;
        StatusId =
            (int)AdmissionTicketTransferStatus.Expired;
        Close(expiredAt);
    }

    private void Close(DateTime occurredAt)
    {
        OpenAdmissionTicketId = Id;
        UpdatedAt = occurredAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private static string NormalizeDigest(string value)
    {
        if (!TryDecodeDigest(value, out _))
        {
            throw new ArgumentException(
                "Capability digest must be canonical SHA-256.",
                nameof(value));
        }

        return value;
    }

    private static bool TryDecodeDigest(
        string value,
        out byte[] digest)
    {
        digest = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        try
        {
            digest = Convert.FromBase64String(value);
            return digest.Length == 32
                && string.Equals(
                    Convert.ToBase64String(digest),
                    value,
                    StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void RequireUuidV7(
        Guid value,
        string parameterName)
    {
        if (value == Guid.Empty
            || value.Version != 7)
        {
            throw new ArgumentException(
                "Identity must be UUIDv7.",
                parameterName);
        }
    }

    private static DateTime EnsureUtc(
        DateTime value,
        string parameterName)
    {
        if (value == default
            || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Timestamp must be UTC.",
                parameterName);
        }

        return value;
    }
}

public sealed class AdmissionTransferDeliveryIntent :
    ITenantEntity,
    IAuditableEntity
{
    private Guid _tenantId;

    private AdmissionTransferDeliveryIntent()
    {
    }

    private AdmissionTransferDeliveryIntent(
        Guid id,
        AdmissionTicketTransfer transfer,
        Guid outboxMessageId,
        DateTime createdAt)
    {
        Id = id;
        TenantId = transfer.TenantId;
        AdmissionTicketTransferId = transfer.Id;
        OutboxMessageId = outboxMessageId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(
            ref _tenantId,
            value,
            nameof(AdmissionTransferDeliveryIntent));
    }

    public Guid AdmissionTicketTransferId { get; private set; }
    public Guid OutboxMessageId { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static AdmissionTransferDeliveryIntent Create(
        Guid id,
        AdmissionTicketTransfer transfer,
        Guid outboxMessageId,
        DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        if (id == Guid.Empty
            || id.Version != 7
            || outboxMessageId == Guid.Empty
            || outboxMessageId.Version != 7
            || createdAt == default
            || createdAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Transfer delivery intent requires UUIDv7 identities and UTC time.");
        }

        return new AdmissionTransferDeliveryIntent(
            id,
            transfer,
            outboxMessageId,
            createdAt);
    }
}
