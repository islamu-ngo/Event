using System.Security.Cryptography;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class AdmissionTicket : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    private readonly List<AdmissionTicketCredential> _credentials = [];
    private Guid _tenantId;

    private AdmissionTicket()
    {
    }

    private AdmissionTicket(
        Guid id,
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderId,
        Guid registrationOrderLineId,
        Guid registrationTicketAssignmentId,
        Guid participantId,
        Guid ticketCatalogVersionId,
        Guid eventTicketTypeId,
        string displayReference,
        Guid? participantSubjectUserId,
        DateTime issuedAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        EventId = eventId;
        RegistrationOrderId = registrationOrderId;
        RegistrationOrderLineId = registrationOrderLineId;
        RegistrationTicketAssignmentId = registrationTicketAssignmentId;
        ParticipantId = participantId;
        TicketCatalogVersionId = ticketCatalogVersionId;
        EventTicketTypeId = eventTicketTypeId;
        HolderSubjectUserId = participantSubjectUserId;
        DisplayReference = displayReference;
        AdmissionTicketStatusId = (int)AdmissionTicketStatusEnum.Active;
        LastTransitionReasonId = (int)AdmissionTicketTransitionReasonEnum.Issued;
        LastTransitionAt = issuedAtUtc;
        CreatedAt = issuedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        private set => SetTenantId(value);
    }

    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => SetTenantId(value);
    }

    public Guid EventId { get; private set; }
    public Guid RegistrationOrderId { get; private set; }
    public Guid RegistrationOrderLineId { get; private set; }
    public Guid RegistrationTicketAssignmentId { get; private set; }
    public Guid ParticipantId { get; private set; }
    public Guid? HolderSubjectUserId { get; private set; }
    public int TransferHopCount { get; private set; }
    public int CredentialGeneration =>
        _credentials.Count == 0
            ? 0
            : _credentials.Max(
                credential =>
                    credential.CredentialVersion);
    public bool IsActive =>
        AdmissionTicketStatusId ==
        (int)AdmissionTicketStatusEnum.Active;
    public Guid TicketCatalogVersionId { get; private set; }
    public Guid EventTicketTypeId { get; private set; }
    public string DisplayReference { get; private set; } = string.Empty;
    public int AdmissionTicketStatusId { get; private set; }
    public AdmissionTicketStatus? AdmissionTicketStatus { get; private set; }
    public int LastTransitionReasonId { get; private set; }
    public AdmissionTicketTransitionReason? LastTransitionReason { get; private set; }
    public DateTime LastTransitionAt { get; private set; }
    public IReadOnlyCollection<AdmissionTicketCredential> Credentials => _credentials.AsReadOnly();
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static AdmissionTicket Issue(
        RegistrationOrder order,
        RegistrationOrderLine orderLine,
        RegistrationTicketAssignment assignment,
        RegistrationParticipant participant,
        EventTicketCatalogVersion ticketCatalogVersion,
        EventTicketType eventTicketType,
        Guid ticketId,
        string displayReference,
        Guid credentialId,
        int credentialVersion,
        int lookupKeyVersion,
        string lookupDigest,
        DateTime issuedAtUtc)
    {
        ValidateAuthority(order, orderLine, assignment, participant, ticketCatalogVersion, eventTicketType);
        RequireUuidV7(ticketId, nameof(ticketId));
        RequireUuidV7(credentialId, nameof(credentialId));
        string normalizedDisplayReference = NormalizeDisplayReference(displayReference);
        ValidateCredentialVersions(credentialVersion, lookupKeyVersion);
        if (credentialVersion != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(credentialVersion), "Initial admission credential version must be one.");
        }

        string normalizedDigest = RegistrationSha256Hash.Normalize(
            lookupDigest,
            nameof(lookupDigest),
            "Admission credential lookup digest");
        DateTime issuedAt = EnsureUtc(issuedAtUtc, nameof(issuedAtUtc));

        AdmissionTicket ticket = new(
            ticketId,
            order.TenantId,
            order.EventId,
            order.Id,
            orderLine.Id,
            assignment.Id,
            participant.Id,
            ticketCatalogVersion.Id,
            eventTicketType.Id,
            normalizedDisplayReference,
            participant.LinkedUserId,
            issuedAt);
        ticket._credentials.Add(new AdmissionTicketCredential(
            credentialId,
            order.TenantId,
            ticketId,
            credentialVersion,
            lookupKeyVersion,
            normalizedDigest,
            issuedAt));
        return ticket;
    }

    public void RotateCredential(
        Guid credentialId,
        int credentialVersion,
        int lookupKeyVersion,
        string lookupDigest,
        DateTime rotatedAtUtc)
    {
        RequireUuidV7(credentialId, nameof(credentialId));
        if (_credentials.Any(credential => credential.Id == credentialId))
        {
            throw new ArgumentException("A new admission credential identity is required.", nameof(credentialId));
        }

        EnsureNonTerminal();
        AdmissionTicketCredential current = CurrentCredential();
        ValidateCredentialVersions(credentialVersion, lookupKeyVersion);
        if (credentialVersion != current.CredentialVersion + 1 || lookupKeyVersion < current.LookupKeyVersion)
        {
            throw new ArgumentException("Credential rotation must append the next credential version without regressing the lookup key version.");
        }

        string normalizedDigest = RegistrationSha256Hash.Normalize(
            lookupDigest,
            nameof(lookupDigest),
            "Admission credential lookup digest");
        DateTime rotatedAt = EnsureForwardTimestamp(rotatedAtUtc, nameof(rotatedAtUtc));
        AdmissionTicketCredential replacement = new(
            credentialId,
            TenantId,
            Id,
            credentialVersion,
            lookupKeyVersion,
            normalizedDigest,
            rotatedAt);

        current.Revoke(rotatedAt);
        _credentials.Add(replacement);
        RecordMutation(AdmissionTicketTransitionReasonEnum.CredentialRotated, rotatedAt);
    }

    public void AcceptTransfer(
        AdmissionTicketTransfer transfer,
        RegistrationParticipant recipient,
        Guid recipientSubjectUserId,
        Guid credentialId,
        int credentialGeneration,
        int lookupKeyVersion,
        string lookupDigest,
        DateTime acceptedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentNullException.ThrowIfNull(recipient);
        transfer.EnsureCanAccept(
            this,
            recipient,
            recipientSubjectUserId,
            credentialGeneration,
            acceptedAtUtc);
        RotateCredential(
            credentialId,
            credentialGeneration,
            lookupKeyVersion,
            lookupDigest,
            acceptedAtUtc);
        transfer.Accept(
            this,
            recipient,
            recipientSubjectUserId,
            credentialGeneration,
            acceptedAtUtc);
        ParticipantId = recipient.Id;
        HolderSubjectUserId = recipientSubjectUserId;
        TransferHopCount = transfer.TransferHop;
    }

    public bool ValidatesCredential(int credentialVersion, int lookupKeyVersion, string lookupDigest)
    {
        if ((AdmissionTicketStatusEnum)AdmissionTicketStatusId != AdmissionTicketStatusEnum.Active ||
            credentialVersion <= 0 || lookupKeyVersion <= 0 ||
            !TryDecodeCanonicalDigest(lookupDigest, out byte[] candidateDigest))
        {
            return false;
        }

        AdmissionTicketCredential? current = _credentials.SingleOrDefault(credential =>
            credential.AdmissionTicketCredentialStatusId == (int)AdmissionTicketCredentialStatusEnum.Active);
        if (current is null || current.CredentialVersion != credentialVersion || current.LookupKeyVersion != lookupKeyVersion)
        {
            return false;
        }

        byte[] storedDigest = Convert.FromBase64String(current.LookupDigest);
        return CryptographicOperations.FixedTimeEquals(storedDigest, candidateDigest);
    }

    public void TransitionTo(AdmissionTicketStatusEnum status, DateTime occurredAtUtc)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        DateTime occurredAt = EnsureForwardTimestamp(occurredAtUtc, nameof(occurredAtUtc));
        AdmissionTicketStatusEnum current = (AdmissionTicketStatusEnum)AdmissionTicketStatusId;
        if (current == status)
        {
            return;
        }

        if (!CanTransition(current, status))
        {
            throw new InvalidOperationException($"Admission ticket cannot transition from {current} to {status}.");
        }

        if (IsTerminal(status))
        {
            RevokeCurrentCredential(occurredAt);
        }

        AdmissionTicketStatusId = (int)status;
        RecordMutation(ReasonFor(status), occurredAt);
    }

    public void Cancel(DateTime cancelledAtUtc) => TransitionTo(AdmissionTicketStatusEnum.Cancelled, cancelledAtUtc);

    public void ApplyRefundAllocations(
        IReadOnlyCollection<AdmissionRefundLineAllocation> allocations,
        DateTime appliedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        DateTime appliedAt = EnsureForwardTimestamp(appliedAtUtc, nameof(appliedAtUtc));
        AdmissionRefundLineAllocation[] facts = allocations.ToArray();
        if (facts.Any(static allocation => allocation is null))
        {
            throw new ArgumentException("Refund allocations cannot contain null facts.", nameof(allocations));
        }

        bool fullyRefundsThisAdmission = facts.Any(allocation =>
            allocation.IsAdmissionRelevant &&
            allocation.RegistrationTicketAssignmentId == RegistrationTicketAssignmentId &&
            allocation.RegistrationOrderLineId == RegistrationOrderLineId &&
            allocation.AcceptedAmountMinor > 0 &&
            allocation.RefundedAmountMinor == allocation.AcceptedAmountMinor);
        if (!fullyRefundsThisAdmission || IsTerminal((AdmissionTicketStatusEnum)AdmissionTicketStatusId))
        {
            return;
        }

        RevokeCurrentCredential(appliedAt);
        AdmissionTicketStatusId = (int)AdmissionTicketStatusEnum.Revoked;
        RecordMutation(AdmissionTicketTransitionReasonEnum.FullyRefunded, appliedAt);
    }

    private static void ValidateAuthority(
        RegistrationOrder order,
        RegistrationOrderLine orderLine,
        RegistrationTicketAssignment assignment,
        RegistrationParticipant participant,
        EventTicketCatalogVersion ticketCatalogVersion,
        EventTicketType eventTicketType)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(orderLine);
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(ticketCatalogVersion);
        ArgumentNullException.ThrowIfNull(eventTicketType);

        if (order.RegistrationOrderStatusId != (int)RegistrationOrderStatusEnum.Confirmed || order.ConfirmedAt is null)
        {
            throw new InvalidOperationException("Admission tickets require a confirmed registration order.");
        }

        bool catalogMatches = ticketCatalogVersion.Id == order.TicketCatalogVersionId &&
            ticketCatalogVersion.TenantId == order.TenantId &&
            ticketCatalogVersion.EventId == order.EventId &&
            ticketCatalogVersion.TicketCatalogStatusId is
                (int)TicketCatalogStatusEnum.Published or (int)TicketCatalogStatusEnum.Retired &&
            ticketCatalogVersion.TicketTypes.Contains(eventTicketType);
        bool lineMatches = order.Lines.Contains(orderLine) &&
            orderLine.RegistrationOrderId == order.Id &&
            orderLine.TenantId == order.TenantId &&
            orderLine.TicketCatalogVersionId == ticketCatalogVersion.Id &&
            orderLine.TicketTypeId == eventTicketType.Id;
        bool ticketTypeMatches = eventTicketType.TenantId == order.TenantId &&
            eventTicketType.CatalogId == ticketCatalogVersion.Id;
        bool participantMatches = order.Participants.Contains(participant) &&
            ReferenceEquals(participant.RegistrationOrder, order) &&
            participant.TenantId == order.TenantId &&
            participant.RegistrationOrderId == order.Id &&
            !participant.IsDeleted;
        bool assignmentMatches = orderLine.Assignments.Contains(assignment) &&
            ReferenceEquals(assignment.RegistrationOrder, order) &&
            ReferenceEquals(assignment.RegistrationOrderLine, orderLine) &&
            ReferenceEquals(assignment.Participant, participant) &&
            assignment.TenantId == order.TenantId &&
            assignment.RegistrationOrderId == order.Id &&
            assignment.RegistrationOrderLineId == orderLine.Id &&
            assignment.AssignmentStatusId == (int)AssignmentStatusEnum.Assigned &&
            assignment.ParticipantId == participant.Id;

        if (!catalogMatches || !lineMatches || !ticketTypeMatches || !participantMatches || !assignmentMatches)
        {
            throw new ArgumentException("Admission authority lineage is inconsistent.");
        }
    }

    private static bool CanTransition(AdmissionTicketStatusEnum current, AdmissionTicketStatusEnum desired) => current switch
    {
        AdmissionTicketStatusEnum.Active => desired is AdmissionTicketStatusEnum.Suspended or
            AdmissionTicketStatusEnum.Revoked or AdmissionTicketStatusEnum.Cancelled or
            AdmissionTicketStatusEnum.Transferred or AdmissionTicketStatusEnum.Expired,
        AdmissionTicketStatusEnum.Suspended => desired is AdmissionTicketStatusEnum.Active or
            AdmissionTicketStatusEnum.Revoked or AdmissionTicketStatusEnum.Cancelled or
            AdmissionTicketStatusEnum.Transferred or AdmissionTicketStatusEnum.Expired,
        AdmissionTicketStatusEnum.Revoked or AdmissionTicketStatusEnum.Cancelled or
            AdmissionTicketStatusEnum.Transferred or AdmissionTicketStatusEnum.Expired => false,
        _ => false
    };

    private static bool IsTerminal(AdmissionTicketStatusEnum status) => status is
        AdmissionTicketStatusEnum.Revoked or AdmissionTicketStatusEnum.Cancelled or
        AdmissionTicketStatusEnum.Transferred or AdmissionTicketStatusEnum.Expired;

    private static AdmissionTicketTransitionReasonEnum ReasonFor(AdmissionTicketStatusEnum status) => status switch
    {
        AdmissionTicketStatusEnum.Active => AdmissionTicketTransitionReasonEnum.Reactivated,
        AdmissionTicketStatusEnum.Suspended => AdmissionTicketTransitionReasonEnum.Suspended,
        AdmissionTicketStatusEnum.Revoked => AdmissionTicketTransitionReasonEnum.Revoked,
        AdmissionTicketStatusEnum.Cancelled => AdmissionTicketTransitionReasonEnum.Cancelled,
        AdmissionTicketStatusEnum.Transferred => AdmissionTicketTransitionReasonEnum.Transferred,
        AdmissionTicketStatusEnum.Expired => AdmissionTicketTransitionReasonEnum.Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static string NormalizeDisplayReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Admission ticket display reference is required.", nameof(value));
        }

        string normalized = value.Trim();
        if (normalized.Length > 100)
        {
            throw new ArgumentException("Admission ticket display reference is too long.", nameof(value));
        }

        return normalized;
    }

    private static void ValidateCredentialVersions(int credentialVersion, int lookupKeyVersion)
    {
        if (credentialVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(credentialVersion));
        }

        if (lookupKeyVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lookupKeyVersion));
        }
    }

    private static bool TryDecodeCanonicalDigest(string value, out byte[] digest)
    {
        digest = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            digest = Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return false;
        }

        return digest.Length == 32 && string.Equals(Convert.ToBase64String(digest), value, StringComparison.Ordinal);
    }

    private static void RequireUuidV7(Guid value, string parameterName)
    {
        if (value == Guid.Empty || value.Version != 7 || value.Variant is < 8 or > 11)
        {
            throw new ArgumentException("Identifier must be an RFC 4122 UUIDv7 value.", parameterName);
        }
    }

    private void SetTenantId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Tenant identity is required.", nameof(TenantId));
        }

        if (_tenantId != Guid.Empty && _tenantId != value)
        {
            throw new InvalidOperationException("Admission ticket tenant identity is immutable.");
        }

        _tenantId = value;
    }

    private void EnsureNonTerminal()
    {
        if (IsTerminal((AdmissionTicketStatusEnum)AdmissionTicketStatusId))
        {
            throw new InvalidOperationException("Terminal admission tickets cannot rotate credentials.");
        }
    }

    private AdmissionTicketCredential CurrentCredential() => _credentials.Single(credential =>
        credential.AdmissionTicketCredentialStatusId == (int)AdmissionTicketCredentialStatusEnum.Active);

    private void RevokeCurrentCredential(DateTime revokedAtUtc)
    {
        AdmissionTicketCredential? current = _credentials.SingleOrDefault(credential =>
            credential.AdmissionTicketCredentialStatusId == (int)AdmissionTicketCredentialStatusEnum.Active);
        current?.Revoke(revokedAtUtc);
    }

    private void RecordMutation(AdmissionTicketTransitionReasonEnum reason, DateTime occurredAtUtc)
    {
        LastTransitionReasonId = (int)reason;
        LastTransitionAt = occurredAtUtc;
        UpdatedAt = occurredAtUtc;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private DateTime EnsureForwardTimestamp(DateTime value, string parameterName)
    {
        DateTime utc = EnsureUtc(value, parameterName);
        if (utc < LastTransitionAt)
        {
            throw new ArgumentException("Admission lifecycle timestamps cannot move backwards.", parameterName);
        }

        return utc;
    }

    private static DateTime EnsureUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", parameterName);
        }

        return value;
    }
}
