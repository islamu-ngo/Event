// ABOUTME: Defines the tenant-scoped registration-order aggregate for buyer, ticket-line, and pre-payment workflow facts.
// ABOUTME: Keeps purchaser PII and platform contributions separate while pinning participation, catalog, and totals snapshots.

using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.Services.Registration;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class RegistrationOrder : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private readonly List<RegistrationOrderLine> _lines = [];
    private readonly List<RegistrationOrderAddOnLine> _addOnLines = [];
    private readonly List<RegistrationParticipant> _participants = [];
    private Guid _tenantId;

    private RegistrationOrder()
    {
    }

    private RegistrationOrder(
        Guid id,
        Guid tenantId,
        Guid eventId,
        Guid? accountUserId,
        Guid? purchaserActorId,
        BookingPartyTypeEnum bookingPartyType,
        Guid ticketCatalogVersionId,
        RegistrationParticipationSnapshot participationSnapshot,
        Guid? registrationWorkflowVersionId,
        CapabilityTokenHash? guestAccessTokenHash,
        string currencyCode,
        DateTime createdAt,
        DateTime? expiresAt)
    {
        Id = id;
        TenantId = tenantId;
        EventId = eventId;
        AccountUserId = accountUserId;
        PurchaserActorId = purchaserActorId;
        BookingPartyTypeId = (int)bookingPartyType;
        TicketCatalogVersionId = ticketCatalogVersionId;
        ParticipationSnapshot = participationSnapshot;
        ParticipationConfigurationVersionSnapshot = participationSnapshot.ConfigurationVersion;
        RegistrationWorkflowVersionId = registrationWorkflowVersionId;
        GuestAccessTokenHash = guestAccessTokenHash;
        CurrencyCode = currencyCode;
        RegistrationOrderStatusId = (int)RegistrationOrderStatusEnum.Draft;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(RegistrationOrder));
    }

    public Guid EventId { get; private set; }

    public Guid? AccountUserId { get; private set; }

    public Guid? PurchaserActorId { get; private set; }

    public int BookingPartyTypeId { get; private set; }

    public BookingPartyType? BookingPartyType { get; private set; }

    public int RegistrationOrderStatusId { get; private set; }

    public RegistrationOrderStatus? RegistrationOrderStatus { get; private set; }

    public Guid TicketCatalogVersionId { get; private set; }

    public Guid? AddOnCatalogVersionIdSnapshot { get; private set; }

    public RegistrationParticipationSnapshot ParticipationSnapshot { get; private set; } = null!;

    public Guid ParticipationConfigurationVersionSnapshot { get; private set; }

    public Guid? RegistrationWorkflowVersionId { get; private set; }

    public CapabilityTokenHash? GuestAccessTokenHash { get; private set; }

    public DateTime? GuestStatusAccessUntilUtc { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    public DateTime? ExpiresAt { get; private set; }

    public DateTime? SubmittedAt { get; private set; }

    public DateTime? ConfirmedAt { get; private set; }

    public DateTime? RejectedAt { get; private set; }

    public DateTime? CancelledAt { get; private set; }

    public RegistrationOrderPii? Pii { get; private set; }

    public RegistrationOrderPlatformContribution? PlatformContribution { get; private set; }

    public Guid? AppliedPromotionDefinitionVersionIdSnapshot { get; private set; }

    public Guid? AppliedPromotionCodeIdSnapshot { get; private set; }

    public string? AppliedPromotionDisplayLabelSnapshot { get; private set; }

    public Guid? ActivePromotionReservationId { get; private set; }

    public long PreDiscountOrganizerDirectedTotalMinorSnapshot { get; private set; }

    public long PromotionDiscountTotalMinorSnapshot { get; private set; }

    public long PostDiscountOrganizerDirectedTotalMinorSnapshot { get; private set; }

    public long OrganizerDirectedTotalMinorSnapshot { get; private set; }

    public long PlatformFeeTotalMinorSnapshot { get; private set; }

    public long OrganizerEarningsTotalMinorSnapshot { get; private set; }

    public long PlatformContributionTotalMinorSnapshot { get; private set; }

    public long TotalDueMinorSnapshot { get; private set; }

    public IReadOnlyCollection<RegistrationOrderLine> Lines => _lines.AsReadOnly();

    public IReadOnlyCollection<RegistrationOrderAddOnLine> AddOnLines => _addOnLines.AsReadOnly();

    public long AddOnTotalMinorSnapshot { get; private set; }

    public IReadOnlyCollection<RegistrationParticipant> Participants => _participants.AsReadOnly();

    public Guid ConcurrencyStamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public static RegistrationOrder Create(
        Guid tenantId,
        Guid eventId,
        Guid? accountUserId,
        Guid? purchaserActorId,
        BookingPartyTypeEnum bookingPartyType,
        Guid ticketCatalogVersionId,
        RegistrationParticipationSnapshot participationSnapshot,
        Guid? registrationWorkflowVersionId,
        CapabilityTokenHash? guestAccessTokenHash,
        string currencyCode,
        DateTime createdAt,
        DateTime? expiresAt) => Create(
        Guid.CreateVersion7(),
        tenantId,
        eventId,
        accountUserId,
        purchaserActorId,
        bookingPartyType,
        ticketCatalogVersionId,
        participationSnapshot,
        registrationWorkflowVersionId,
        guestAccessTokenHash,
        currencyCode,
        createdAt,
        expiresAt);

    public static RegistrationOrder Create(
        Guid id,
        Guid tenantId,
        Guid eventId,
        Guid? accountUserId,
        Guid? purchaserActorId,
        BookingPartyTypeEnum bookingPartyType,
        Guid ticketCatalogVersionId,
        RegistrationParticipationSnapshot participationSnapshot,
        Guid? registrationWorkflowVersionId,
        CapabilityTokenHash? guestAccessTokenHash,
        string currencyCode,
        DateTime createdAt,
        DateTime? expiresAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || eventId == Guid.Empty || ticketCatalogVersionId == Guid.Empty ||
            accountUserId == Guid.Empty || purchaserActorId == Guid.Empty || registrationWorkflowVersionId == Guid.Empty ||
            !Enum.IsDefined(bookingPartyType) || (accountUserId is null && guestAccessTokenHash is null))
        {
            throw new ArgumentException("Registration order identity and booking facts are invalid.");
        }

        ArgumentNullException.ThrowIfNull(participationSnapshot);
        DateTime normalizedCreatedAt = EnsureUtc(createdAt, nameof(createdAt));
        DateTime? normalizedExpiresAt = expiresAt.HasValue ? EnsureUtc(expiresAt.Value, nameof(expiresAt)) : null;
        if (normalizedExpiresAt.HasValue && normalizedExpiresAt.Value <= normalizedCreatedAt)
        {
            throw new ArgumentException("Order expiry must be after creation.", nameof(expiresAt));
        }

        return new RegistrationOrder(
            id,
            tenantId,
            eventId,
            accountUserId,
            purchaserActorId,
            bookingPartyType,
            ticketCatalogVersionId,
            participationSnapshot,
            registrationWorkflowVersionId,
            guestAccessTokenHash,
            CurrencyMetadata.Get(currencyCode).Code,
            normalizedCreatedAt,
            normalizedExpiresAt);
    }

    // Allocation establishes the promise before payment is possible. Missing historical promises
    // are not inferred on reads; only an existing live promise may subsequently be extended.
    public bool TryEstablishGuestStatusPromise(DateTimeOffset? lastSessionEndUtc, DateTime utcNow)
    {
        EnsureUtc(utcNow, nameof(utcNow));
        if (GuestAccessTokenHash is null || GuestStatusAccessUntilUtc is not null ||
            RegistrationOrderStatusId != (int)RegistrationOrderStatusEnum.Draft)
        {
            return false;
        }

        DateTime? deadline = GetGuestStatusDeadline(lastSessionEndUtc);
        if (deadline is null || deadline <= utcNow)
        {
            return false;
        }

        GuestStatusAccessUntilUtc = deadline;
        return true;
    }

    public static DateTime? GetGuestStatusDeadline(DateTimeOffset? lastSessionEndUtc) =>
        lastSessionEndUtc is { } end && end.UtcDateTime <= DateTime.MaxValue.AddDays(-30)
            ? end.UtcDateTime.AddDays(30)
            : null;

    public void AddLine(RegistrationOrderLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureCommercialFactsMutable();

        if (line.RegistrationOrderId != Id || line.TenantId != TenantId || line.TicketCatalogVersionId != TicketCatalogVersionId ||
            !string.Equals(line.CurrencyCodeSnapshot, CurrencyCode, StringComparison.Ordinal) ||
            _lines.Any(existing => existing.TicketTypeId == line.TicketTypeId))
        {
            throw new ArgumentException("Order line does not match the pinned order commercial context.", nameof(line));
        }

        _lines.Add(line);
    }

    public void AddAddOnLine(RegistrationOrderAddOnLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureAddOnSelectionMutable();
        if (line.RegistrationOrderId != Id ||
            line.TenantId != TenantId ||
            line.EventId != EventId ||
            line.EventAddOnCatalogVersionId != AddOnCatalogVersionIdSnapshot ||
            !string.Equals(line.CurrencyCodeSnapshot, CurrencyCode, StringComparison.Ordinal) ||
            _addOnLines.Any(existing =>
                existing.Id == line.Id ||
                existing.EventAddOnCatalogItemId == line.EventAddOnCatalogItemId))
        {
            throw new ArgumentException(
                "Add-on line does not match the order's event, tenant, currency, or unique item selection.",
                nameof(line));
        }

        long nextTotal = MinorUnitMath.Add(
            AddOnTotalMinorSnapshot,
            line.LineTotalMinorSnapshot);
        _addOnLines.Add(line);
        AddOnTotalMinorSnapshot = nextTotal;
    }

    public void PinAddOnCatalog(EventAddOnCatalogVersion catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        EnsureAddOnSelectionMutable();
        if (!catalog.IsPublished ||
            catalog.TenantId != TenantId ||
            catalog.EventId != EventId ||
            !string.Equals(catalog.CurrencyCode, CurrencyCode, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The add-on catalog must be the published catalog disclosed for this order.",
                nameof(catalog));
        }

        if (AddOnCatalogVersionIdSnapshot.HasValue &&
            AddOnCatalogVersionIdSnapshot.Value != catalog.Id)
        {
            throw new InvalidOperationException(
                "The order's add-on catalog lineage is already pinned.");
        }

        AddOnCatalogVersionIdSnapshot = catalog.Id;
    }

    public void VerifyCommercialTotals() => _ = GetVerifiedTotalDueSnapshot();

    public void AddParticipant(RegistrationParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        EnsureCommercialFactsMutable();
        if (participant.TenantId != TenantId || participant.RegistrationOrderId != Id ||
            _participants.Any(existing => existing.Id == participant.Id))
        {
            throw new ArgumentException("Participant does not belong to this order or is already attached.", nameof(participant));
        }

        participant.AttachToOrder(this);
        _participants.Add(participant);
    }

    public void AddAssignment(
        RegistrationOrderLine line,
        RegistrationTicketAssignment assignment,
        RegistrationParticipant? participant)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(assignment);
        EnsureCommercialFactsMutable();
        if (!_lines.Contains(line) || assignment.TenantId != TenantId || assignment.RegistrationOrderId != Id ||
            assignment.RegistrationOrderLineId != line.Id ||
            (participant is null) != (assignment.ParticipantId is null) ||
            participant is not null && (!_participants.Contains(participant) || assignment.ParticipantId != participant.Id))
        {
            throw new ArgumentException("Ticket assignment does not belong to this order line and participant graph.", nameof(assignment));
        }

        assignment.AttachToGraph(this, line, participant);
        line.AddAssignment(assignment);
    }

    public void SetPii(RegistrationOrderPii pii)
    {
        ArgumentNullException.ThrowIfNull(pii);
        if (pii.RegistrationOrderId != Id || pii.TenantId != TenantId)
        {
            throw new ArgumentException("Purchaser PII does not belong to this order.", nameof(pii));
        }


        Pii = pii;
    }

    public bool TryLinkGuestOrderToAccount(Guid accountUserId, string verifiedNormalizedEmail)
    {
        if (accountUserId == Guid.Empty || string.IsNullOrWhiteSpace(verifiedNormalizedEmail) || GuestAccessTokenHash is null)
        {
            throw new ArgumentException("Guest order linking requires an account, verified email, and guest order capability.");
        }

        if (!string.Equals(Pii?.NormalizedEmail, verifiedNormalizedEmail.Trim().ToUpperInvariant(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The verified account email does not match the registration order contact email.");
        }

        Pii.MarkEmailVerified(verifiedNormalizedEmail);

        if (AccountUserId == accountUserId)
        {
            return false;
        }

        if (AccountUserId.HasValue)
        {
            throw new InvalidOperationException("Registration order is already linked to another account.");
        }

        AccountUserId = accountUserId;
        BumpConcurrency(Guid.CreateVersion7());
        return true;
    }

    public void SetPlatformContribution(RegistrationOrderPlatformContribution? contribution)
    {
        EnsureCommercialFactsMutable();
        if (contribution is not null &&
            (contribution.RegistrationOrderId != Id || contribution.TenantId != TenantId || contribution.CurrencyCode != CurrencyCode || contribution.AmountMinor < 0))
        {
            throw new ArgumentException("Platform contribution does not match the order.", nameof(contribution));
        }

        PlatformContribution = contribution;
    }

    public void ApplyTotals(RegistrationOrderTotalsSnapshot totals)
    {
        ArgumentNullException.ThrowIfNull(totals);
        EnsureCommercialFactsMutable();

        long ticketLineTotal = _lines.Aggregate(
            0L,
            static (total, line) =>
                MinorUnitMath.Add(total, line.PostDiscountLineSubtotalMinorSnapshot));
        long lineTotal = MinorUnitMath.Add(ticketLineTotal, AddOnTotalMinorSnapshot);
        long contributionTotal = PlatformContribution?.AmountMinor ?? 0;
        if (!string.Equals(totals.CurrencyCode, CurrencyCode, StringComparison.Ordinal) ||
            totals.OrganizerDirectedTotalMinor != lineTotal ||
            totals.PlatformFeeTotalMinor < 0 ||
            totals.PlatformFeeTotalMinor > ticketLineTotal ||
            totals.PlatformContributionTotalMinor != contributionTotal)
        {
            throw new ArgumentException("Order totals do not match the pinned order snapshots.", nameof(totals));
        }

        long ticketPreDiscountTotal = _lines.Aggregate(
            0L,
            static (total, line) =>
                MinorUnitMath.Add(total, line.PreDiscountLineSubtotalMinorSnapshot));
        PreDiscountOrganizerDirectedTotalMinorSnapshot =
            MinorUnitMath.Add(ticketPreDiscountTotal, AddOnTotalMinorSnapshot);
        PromotionDiscountTotalMinorSnapshot = _lines.Aggregate(0L, static (total, line) => MinorUnitMath.Add(total, line.PromotionDiscountAmountMinorSnapshot));
        PostDiscountOrganizerDirectedTotalMinorSnapshot = totals.OrganizerDirectedTotalMinor;
        OrganizerDirectedTotalMinorSnapshot = totals.OrganizerDirectedTotalMinor;
        PlatformFeeTotalMinorSnapshot = totals.PlatformFeeTotalMinor;
        OrganizerEarningsTotalMinorSnapshot = totals.OrganizerEarningsTotalMinor;
        PlatformContributionTotalMinorSnapshot = totals.PlatformContributionTotalMinor;
        TotalDueMinorSnapshot = totals.TotalDueMinor;
    }

    public bool ApplyPromotion(
        PromotionReservation reservation,
        PromotionDefinition definition,
        PromotionCode code,
        DateTime evaluatedAtUtc,
        int currentTotalRedemptions,
        int currentPurchaserRedemptions,
        PlatformFeePolicy? feePolicy)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(code);
        EnsureCommercialFactsMutable();
        EnsurePromotionScopeMatchesOrder(definition, code);
        EnsureFeePolicyMatchesPinnedLines(feePolicy);

        if (reservation.RegistrationOrderId != Id || reservation.TenantId != TenantId || reservation.PromotionDefinitionVersionId != definition.Id ||
            reservation.PromotionCodeId != code.Id || reservation.PromotionReservationStatusId != (int)PromotionReservationStatusEnum.Active ||
            reservation.OrderReservationSlot != Guid.Empty)
        {
            throw new ArgumentException("Active promotion reservation does not match the order.", nameof(reservation));
        }

        if (ActivePromotionReservationId == reservation.Id && AppliedPromotionCodeIdSnapshot == code.Id)
        {
            return false;
        }

        if (ActivePromotionReservationId.HasValue || AppliedPromotionCodeIdSnapshot.HasValue)
        {
            throw new InvalidOperationException("Remove the active promotion before applying another code.");
        }

        PromotionDiscountAllocation allocation = PromotionDiscountAllocator.Allocate(
            definition,
            _lines.Select(line => new PromotionDiscountLine(line.Id, line.TicketTypeId, line.CurrencyCodeSnapshot, line.LineSubtotalSnapshot)).ToArray(),
            evaluatedAtUtc,
            currentTotalRedemptions,
            currentPurchaserRedemptions);
        foreach (PromotionLineDiscountAllocation lineAllocation in allocation.LineAllocations)
        {
            _lines.Single(line => line.Id == lineAllocation.LineId).ApplyPromotionDiscount(lineAllocation);
        }

        AppliedPromotionDefinitionVersionIdSnapshot = definition.Id;
        AppliedPromotionCodeIdSnapshot = code.Id;
        AppliedPromotionDisplayLabelSnapshot = code.DisplayLabel;
        ActivePromotionReservationId = reservation.Id;
        RepriceFromCurrentLines(feePolicy);
        return true;
    }

    public bool RemovePromotion(PromotionReservation reservation, DateTime releasedAtUtc, PlatformFeePolicy? feePolicy)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        EnsureCommercialFactsMutable();

        if (AppliedPromotionCodeIdSnapshot is null && ActivePromotionReservationId is null)
        {
            return false;
        }

        if (ActivePromotionReservationId != reservation.Id || reservation.RegistrationOrderId != Id || reservation.TenantId != TenantId)
        {
            throw new InvalidOperationException("The active promotion reservation must be released before another code can be applied.");
        }

        EnsureFeePolicyMatchesPinnedLines(feePolicy);

        reservation.TryRelease(releasedAtUtc);
        foreach (RegistrationOrderLine line in _lines)
        {
            line.ClearPromotionDiscount();
        }

        AppliedPromotionDefinitionVersionIdSnapshot = null;
        AppliedPromotionCodeIdSnapshot = null;
        AppliedPromotionDisplayLabelSnapshot = null;
        ActivePromotionReservationId = null;
        RepriceFromCurrentLines(feePolicy);
        return true;
    }

    public VerifiedPurchaserIdentity? GetVerifiedPurchaserIdentity()
    {
        if (AccountUserId.HasValue)
        {
            return VerifiedPurchaserIdentity.Account(AccountUserId.Value);
        }

        if (Pii is { IsEmailVerified: true, NormalizedEmail: not null })
        {
            return VerifiedPurchaserIdentity.Email(Pii.NormalizedEmail);
        }

        return PurchaserActorId.HasValue ? VerifiedPurchaserIdentity.Actor(PurchaserActorId.Value) : null;
    }

    public void TransitionTo(RegistrationOrderStatusEnum desiredStatus, DateTime timestamp)
    {
        DateTime utcTimestamp = EnsureUtc(timestamp, nameof(timestamp));
        RegistrationOrderStatusEnum currentStatus = (RegistrationOrderStatusEnum)RegistrationOrderStatusId;
        RegistrationOrderRules.EnsureCanTransition(currentStatus, desiredStatus);

        if (currentStatus == desiredStatus)
        {
            return;
        }

        if (currentStatus == RegistrationOrderStatusEnum.ReadyForCheckout &&
            desiredStatus is RegistrationOrderStatusEnum.Confirmed or RegistrationOrderStatusEnum.AwaitingPayment &&
            desiredStatus != RegistrationOrderRules.GetCheckoutDestination(GetVerifiedTotalDueSnapshot()))
        {
            throw new InvalidOperationException("The order checkout destination does not match its snapshotted total.");
        }

        RegistrationOrderStatusId = (int)desiredStatus;

        if (desiredStatus is RegistrationOrderStatusEnum.AwaitingPayment or RegistrationOrderStatusEnum.AwaitingApproval or RegistrationOrderStatusEnum.Confirmed)
        {
            SubmittedAt ??= utcTimestamp;
        }

        if (desiredStatus == RegistrationOrderStatusEnum.Confirmed)
        {
            ConfirmedAt = utcTimestamp;
        }
        else if (desiredStatus == RegistrationOrderStatusEnum.Rejected)
        {
            RejectedAt = utcTimestamp;
        }
        else if (desiredStatus == RegistrationOrderStatusEnum.Cancelled)
        {
            CancelledAt = utcTimestamp;
        }
    }

    public bool CanTransitionTo(RegistrationOrderStatusEnum desiredStatus) =>
        RegistrationOrderRules.CanTransition(
            (RegistrationOrderStatusEnum)RegistrationOrderStatusId,
            desiredStatus);

    public bool TryTransitionFrom(
        RegistrationOrderStatusEnum expectedStatus,
        RegistrationOrderStatusEnum desiredStatus,
        DateTime timestamp)
    {
        if ((RegistrationOrderStatusEnum)RegistrationOrderStatusId != expectedStatus ||
            !CanTransitionTo(desiredStatus))
        {
            return false;
        }

        TransitionTo(desiredStatus, timestamp);
        return true;
    }

    public bool TryBeginHoldExpiryRecovery(DateTime timestamp)
    {
        RegistrationOrderStatusEnum currentStatus = (RegistrationOrderStatusEnum)RegistrationOrderStatusId;
        if (currentStatus == RegistrationOrderStatusEnum.NeedsReconciliation ||
            !RegistrationOrderRules.CanTransition(currentStatus, RegistrationOrderStatusEnum.NeedsReconciliation))
        {
            return false;
        }

        TransitionTo(RegistrationOrderStatusEnum.NeedsReconciliation, timestamp);
        return true;
    }

    public bool ExtendPaymentCutoff(DateTime cutoff, DateTime changedAt)
    {
        DateTime utcCutoff = EnsureUtc(cutoff, nameof(cutoff));
        DateTime utcChangedAt = EnsureUtc(changedAt, nameof(changedAt));
        if ((RegistrationOrderStatusEnum)RegistrationOrderStatusId != RegistrationOrderStatusEnum.AwaitingPayment ||
            ExpiresAt is not { } current || utcCutoff <= current)
        {
            return false;
        }

        ExpiresAt = utcCutoff;
        UpdatedAt = utcChangedAt;
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }

    public bool TryResolveHoldExpiryRecovery(bool capacityReReserved, DateTime timestamp)
    {
        if ((RegistrationOrderStatusEnum)RegistrationOrderStatusId != RegistrationOrderStatusEnum.NeedsReconciliation)
        {
            return false;
        }

        TransitionTo(
            capacityReReserved
                ? RegistrationOrderStatusEnum.ReadyForCheckout
                : RegistrationOrderStatusEnum.Waitlisted,
            timestamp);
        return true;
    }

    public void BumpConcurrency(Guid concurrencyStamp)
    {
        if (concurrencyStamp == Guid.Empty)
        {
            throw new ArgumentException("Order concurrency stamp is required.", nameof(concurrencyStamp));
        }

        ConcurrencyStamp = concurrencyStamp;
    }

    private void EnsureCommercialFactsMutable()
    {
        if (RegistrationOrderRules.IsTerminalForCurrentWorkstream((RegistrationOrderStatusEnum)RegistrationOrderStatusId))
        {
            throw new InvalidOperationException("Commercial facts are frozen after the order reaches its current workflow boundary.");
        }
    }

    private void EnsureAddOnSelectionMutable()
    {
        EnsureCommercialFactsMutable();
        if ((RegistrationOrderStatusEnum)RegistrationOrderStatusId !=
            RegistrationOrderStatusEnum.Draft)
        {
            throw new InvalidOperationException(
                "Add-on selection is allowed only during the original draft checkout.");
        }
    }

    private long GetVerifiedTotalDueSnapshot()
    {
        long ticketLineTotal = _lines.Aggregate(
            0L,
            static (total, line) =>
                MinorUnitMath.Add(total, line.PostDiscountLineSubtotalMinorSnapshot));
        long lineTotal = MinorUnitMath.Add(ticketLineTotal, AddOnTotalMinorSnapshot);
        long contributionTotal = PlatformContribution?.AmountMinor ?? 0;
        long expectedTotalDue = MinorUnitMath.Add(lineTotal, contributionTotal);

        if (OrganizerDirectedTotalMinorSnapshot != lineTotal ||
            PlatformContributionTotalMinorSnapshot != contributionTotal ||
            TotalDueMinorSnapshot != expectedTotalDue ||
            PlatformFeeTotalMinorSnapshot < 0 ||
            PlatformFeeTotalMinorSnapshot > ticketLineTotal ||
            OrganizerEarningsTotalMinorSnapshot != lineTotal - PlatformFeeTotalMinorSnapshot)
        {
            throw new InvalidOperationException("Order totals must match the immutable line and contribution snapshots before checkout.");
        }

        return TotalDueMinorSnapshot;
    }

    private static DateTime EnsureUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", parameterName);
        }

        return value;
    }

    private void EnsurePromotionScopeMatchesOrder(PromotionDefinition definition, PromotionCode code)
    {
        if (definition.TenantId != TenantId || code.TenantId != TenantId ||
            code.PromotionDefinitionVersionId != definition.Id ||
            definition.ScopeMetadata != code.ScopeMetadata ||
            definition.ScopeMetadata.EventId != EventId ||
            definition.ScopeMetadata.TicketCatalogVersionId != TicketCatalogVersionId ||
            !string.Equals(definition.ScopeMetadata.CurrencyCode, CurrencyCode, StringComparison.Ordinal))
        {
            throw new ArgumentException("Promotion scope must match the order event and ticket catalog version.");
        }
    }

    private void EnsureFeePolicyMatchesPinnedLines(PlatformFeePolicy? feePolicy)
    {
        int?[] pinnedVersions = _lines.Select(static line => line.PlatformFeePolicyVersionSnapshot).Distinct().ToArray();
        if (pinnedVersions.Length > 1)
        {
            throw new InvalidOperationException("Order lines must agree on one pinned platform fee policy version.");
        }

        int? pinnedVersion = pinnedVersions.SingleOrDefault();
        int? suppliedVersion = feePolicy?.VersionNumber;
        if (pinnedVersion != suppliedVersion)
        {
            throw new InvalidOperationException("Supplied platform fee policy version must match the order lines' pinned version.");
        }
    }

    private void RepriceFromCurrentLines(PlatformFeePolicy? feePolicy)
    {
        long ticketPostDiscountTotal = _lines.Aggregate(
            0L,
            static (total, line) =>
                MinorUnitMath.Add(total, line.PostDiscountLineSubtotalMinorSnapshot));
        long postDiscountOrganizerTotal =
            MinorUnitMath.Add(ticketPostDiscountTotal, AddOnTotalMinorSnapshot);
        PlatformContribution?.Reprice(postDiscountOrganizerTotal);
        long platformFee =
            feePolicy?.CalculateFeeMinor(CurrencyCode, ticketPostDiscountTotal) ?? 0;
        ApplyTotals(RegistrationOrderTotalsSnapshot.Create(
            CurrencyCode,
            postDiscountOrganizerTotal,
            platformFee,
            postDiscountOrganizerTotal - platformFee,
            PlatformContribution?.AmountMinor ?? 0));
    }
}
