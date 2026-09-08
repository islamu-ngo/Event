using Explore.Domain.Interfaces;

namespace Explore.Domain;

public enum FairReturnSupplyStatus
{
    Available = 1,
    Bound = 2,
    Withdrawn = 3,
}

public enum EventWaitlistEntryStatus
{
    Queued = 1,
    Offered = 2,
    Converted = 3,
    Withdrawn = 4,
}

public enum EventWaitlistOfferStatus
{
    Active = 1,
    Expired = 2,
    Finalized = 3,
}

public enum FairReturnOutcome
{
    Allocated = 1,
    SourceSubstituted = 2,
    PaymentHandoffWon = 3,
    PrivateConflict = 4,
    NoCommerciallyEquivalentSupply = 5,
    OfferExpired = 6,
    ReplacementFinalized = 7,
    StaleObservation = 8,
    AlreadyApplied = 9,
    Withdrawn = 10,
}

public sealed class FairReturnSupplyPolicy :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private Guid _tenantId;

    private FairReturnSupplyPolicy()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(
            ref _tenantId,
            value,
            nameof(FairReturnSupplyPolicy));
    }
    public Guid EventId { get; private set; }
    public Guid TicketCatalogVersionId { get; private set; }
    public Guid EventTicketTypeId { get; private set; }
    public bool IsEnabled { get; private set; }
    public int OfferLifetimeMinutes { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static FairReturnSupplyPolicy Create(
        Guid id,
        Guid tenantId,
        Guid eventId,
        Guid ticketCatalogVersionId,
        Guid eventTicketTypeId,
        bool isEnabled,
        int offerLifetimeMinutes,
        DateTime createdAtUtc)
    {
        RequireUuidV7(id, nameof(id));
        RequireUuidV7(tenantId, nameof(tenantId));
        RequireUuidV7(eventId, nameof(eventId));
        RequireUuidV7(
            ticketCatalogVersionId,
            nameof(ticketCatalogVersionId));
        RequireUuidV7(
            eventTicketTypeId,
            nameof(eventTicketTypeId));
        if (offerLifetimeMinutes is < 5 or > 43_200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offerLifetimeMinutes));
        }
        DateTime createdAt = RequireUtc(
            createdAtUtc,
            nameof(createdAtUtc));
        return new FairReturnSupplyPolicy
        {
            Id = id,
            TenantId = tenantId,
            EventId = eventId,
            TicketCatalogVersionId =
                ticketCatalogVersionId,
            EventTicketTypeId = eventTicketTypeId,
            IsEnabled = isEnabled,
            OfferLifetimeMinutes =
                offerLifetimeMinutes,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = createdAt,
        };
    }

    internal static void RequireUuidV7(
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

    internal static DateTime RequireUtc(
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

public sealed class FairReturnSupplyUnit :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private Guid _tenantId;

    private FairReturnSupplyUnit()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(
            ref _tenantId,
            value,
            nameof(FairReturnSupplyUnit));
    }
    public Guid EventId { get; private set; }
    public Guid EventTicketTypeId { get; private set; }
    public Guid TicketCatalogVersionId { get; private set; }
    public Guid PurchasePolicySnapshotId { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public string CommercialTermsDigest { get; private set; } = string.Empty;
    public string AdmissionEntitlementDigest { get; private set; } = string.Empty;
    public long GrossMinorUnits { get; private set; }
    public int RefundFundingModeId { get; private set; }
    public Guid SellerRegistrationOrderLineId { get; private set; }
    public int StatusId { get; private set; }
    public DateTime? BoundAt { get; private set; }
    public DateTime? WithdrawnAt { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static FairReturnSupplyUnit Create(
        Guid id,
        Guid tenantId,
        Guid eventId,
        Guid eventTicketTypeId,
        Guid ticketCatalogVersionId,
        Guid purchasePolicySnapshotId,
        string currencyCode,
        string commercialTermsDigest,
        string admissionEntitlementDigest,
        long grossMinorUnits,
        int refundFundingModeId,
        Guid sellerRegistrationOrderLineId,
        DateTime createdAtUtc)
    {
        FairReturnSupplyPolicy.RequireUuidV7(
            id,
            nameof(id));
        FairReturnSupplyPolicy.RequireUuidV7(
            tenantId,
            nameof(tenantId));
        FairReturnSupplyPolicy.RequireUuidV7(
            eventId,
            nameof(eventId));
        FairReturnSupplyPolicy.RequireUuidV7(
            eventTicketTypeId,
            nameof(eventTicketTypeId));
        FairReturnSupplyPolicy.RequireUuidV7(
            ticketCatalogVersionId,
            nameof(ticketCatalogVersionId));
        FairReturnSupplyPolicy.RequireUuidV7(
            purchasePolicySnapshotId,
            nameof(purchasePolicySnapshotId));
        FairReturnSupplyPolicy.RequireUuidV7(
            sellerRegistrationOrderLineId,
            nameof(sellerRegistrationOrderLineId));
        if (grossMinorUnits < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(grossMinorUnits));
        }
        if (refundFundingModeId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refundFundingModeId));
        }
        return new FairReturnSupplyUnit
        {
            Id = id,
            TenantId = tenantId,
            EventId = eventId,
            EventTicketTypeId = eventTicketTypeId,
            TicketCatalogVersionId =
                ticketCatalogVersionId,
            PurchasePolicySnapshotId =
                purchasePolicySnapshotId,
            CurrencyCode = NormalizeCode(
                currencyCode,
                nameof(currencyCode),
                3),
            CommercialTermsDigest = NormalizeDigest(
                commercialTermsDigest,
                nameof(commercialTermsDigest)),
            AdmissionEntitlementDigest =
                NormalizeDigest(
                    admissionEntitlementDigest,
                    nameof(
                        admissionEntitlementDigest)),
            GrossMinorUnits = grossMinorUnits,
            RefundFundingModeId =
                refundFundingModeId,
            SellerRegistrationOrderLineId =
                sellerRegistrationOrderLineId,
            StatusId =
                (int)FairReturnSupplyStatus.Available,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt =
                FairReturnSupplyPolicy.RequireUtc(
                    createdAtUtc,
                    nameof(createdAtUtc)),
        };
    }

    public bool IsCommerciallyEquivalentTo(
        FairReturnSupplyUnit other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return TenantId == other.TenantId
            && EventId == other.EventId
            && EventTicketTypeId ==
                other.EventTicketTypeId
            && TicketCatalogVersionId ==
                other.TicketCatalogVersionId
            && PurchasePolicySnapshotId ==
                other.PurchasePolicySnapshotId
            && string.Equals(
                CurrencyCode,
                other.CurrencyCode,
                StringComparison.Ordinal)
            && string.Equals(
                CommercialTermsDigest,
                other.CommercialTermsDigest,
                StringComparison.Ordinal)
            && string.Equals(
                AdmissionEntitlementDigest,
                other.AdmissionEntitlementDigest,
                StringComparison.Ordinal)
            && GrossMinorUnits ==
                other.GrossMinorUnits
            && RefundFundingModeId ==
                other.RefundFundingModeId;
    }

    public void Bind(DateTime boundAtUtc)
    {
        if (StatusId !=
            (int)FairReturnSupplyStatus.Available)
        {
            throw new InvalidOperationException(
                "Supply is not available.");
        }
        DateTime boundAt =
            FairReturnSupplyPolicy.RequireUtc(
                boundAtUtc,
                nameof(boundAtUtc));
        StatusId =
            (int)FairReturnSupplyStatus.Bound;
        BoundAt = boundAt;
        Touch(boundAt);
    }

    public void Release(DateTime releasedAtUtc)
    {
        if (StatusId !=
            (int)FairReturnSupplyStatus.Bound)
        {
            throw new InvalidOperationException(
                "Only bound supply can be released.");
        }
        DateTime releasedAt =
            FairReturnSupplyPolicy.RequireUtc(
                releasedAtUtc,
                nameof(releasedAtUtc));
        StatusId =
            (int)FairReturnSupplyStatus.Available;
        BoundAt = null;
        Touch(releasedAt);
    }

    public void Withdraw(DateTime withdrawnAtUtc)
    {
        if (StatusId ==
            (int)FairReturnSupplyStatus.Withdrawn)
        {
            return;
        }
        DateTime withdrawnAt =
            FairReturnSupplyPolicy.RequireUtc(
                withdrawnAtUtc,
                nameof(withdrawnAtUtc));
        StatusId =
            (int)FairReturnSupplyStatus.Withdrawn;
        WithdrawnAt = withdrawnAt;
        Touch(withdrawnAt);
    }

    private void Touch(DateTime occurredAt)
    {
        UpdatedAt = occurredAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    internal static string NormalizeCode(
        string value,
        string parameterName,
        int exactLength)
    {
        string normalized =
            value?.Trim().ToUpperInvariant()
            ?? string.Empty;
        if (normalized.Length != exactLength
            || normalized.Any(character =>
                character is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "Code is invalid.",
                parameterName);
        }
        return normalized;
    }

    internal static string NormalizeDigest(
        string value,
        string parameterName)
    {
        string normalized = value?.Trim()
            ?? string.Empty;
        if (normalized.Length != 44)
        {
            throw new ArgumentException(
                "Digest must be canonical SHA-256 base64.",
                parameterName);
        }
        try
        {
            byte[] bytes =
                Convert.FromBase64String(normalized);
            if (bytes.Length != 32
                || !string.Equals(
                    Convert.ToBase64String(bytes),
                    normalized,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Digest must be canonical SHA-256 base64.",
                    parameterName);
            }
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "Digest must be canonical SHA-256 base64.",
                parameterName,
                exception);
        }
        return normalized;
    }
}

public sealed class EventWaitlistEntry :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private Guid _tenantId;
    private EventWaitlistEntry()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(
            ref _tenantId,
            value,
            nameof(EventWaitlistEntry));
    }
    public Guid EventId { get; private set; }
    public Guid EventTicketTypeId { get; private set; }
    public Guid TicketCatalogVersionId { get; private set; }
    public Guid PurchasePolicySnapshotId { get; private set; }
    public Guid RegistrationOrderId { get; private set; }
    public Guid RegistrationOrderLineId { get; private set; }
    public Guid ParticipantId { get; private set; }
    public Guid BuyerAccountUserId { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public string CommercialTermsDigest { get; private set; } = string.Empty;
    public string AdmissionEntitlementDigest { get; private set; } = string.Empty;
    public long GrossMinorUnits { get; private set; }
    public int RefundFundingModeId { get; private set; }
    public int Priority { get; private set; }
    public DateTime EnqueuedAt { get; private set; }
    public Guid? OpenRegistrationOrderLineId { get; private set; }
    public int StatusId { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static EventWaitlistEntry Enqueue(
        Guid id,
        Guid tenantId,
        Guid eventId,
        Guid eventTicketTypeId,
        Guid ticketCatalogVersionId,
        Guid purchasePolicySnapshotId,
        Guid registrationOrderId,
        Guid registrationOrderLineId,
        Guid participantId,
        Guid buyerAccountUserId,
        string currencyCode,
        string commercialTermsDigest,
        string admissionEntitlementDigest,
        long grossMinorUnits,
        int refundFundingModeId,
        int priority,
        DateTime enqueuedAtUtc)
    {
        FairReturnSupplyPolicy.RequireUuidV7(id, nameof(id));
        FairReturnSupplyPolicy.RequireUuidV7(
            tenantId,
            nameof(tenantId));
        FairReturnSupplyPolicy.RequireUuidV7(
            eventId,
            nameof(eventId));
        FairReturnSupplyPolicy.RequireUuidV7(
            eventTicketTypeId,
            nameof(eventTicketTypeId));
        FairReturnSupplyPolicy.RequireUuidV7(
            ticketCatalogVersionId,
            nameof(ticketCatalogVersionId));
        FairReturnSupplyPolicy.RequireUuidV7(
            purchasePolicySnapshotId,
            nameof(purchasePolicySnapshotId));
        FairReturnSupplyPolicy.RequireUuidV7(
            registrationOrderId,
            nameof(registrationOrderId));
        FairReturnSupplyPolicy.RequireUuidV7(
            registrationOrderLineId,
            nameof(registrationOrderLineId));
        FairReturnSupplyPolicy.RequireUuidV7(
            participantId,
            nameof(participantId));
        FairReturnSupplyPolicy.RequireUuidV7(
            buyerAccountUserId,
            nameof(buyerAccountUserId));
        if (grossMinorUnits < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(grossMinorUnits));
        }
        if (refundFundingModeId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refundFundingModeId));
        }
        DateTime enqueuedAt =
            FairReturnSupplyPolicy.RequireUtc(
                enqueuedAtUtc,
                nameof(enqueuedAtUtc));
        return new EventWaitlistEntry
        {
            Id = id,
            TenantId = tenantId,
            EventId = eventId,
            EventTicketTypeId =
                eventTicketTypeId,
            TicketCatalogVersionId =
                ticketCatalogVersionId,
            PurchasePolicySnapshotId =
                purchasePolicySnapshotId,
            RegistrationOrderId =
                registrationOrderId,
            RegistrationOrderLineId =
                registrationOrderLineId,
            ParticipantId = participantId,
            BuyerAccountUserId =
                buyerAccountUserId,
            CurrencyCode =
                FairReturnSupplyUnit.NormalizeCode(
                    currencyCode,
                    nameof(currencyCode),
                    3),
            CommercialTermsDigest =
                FairReturnSupplyUnit.NormalizeDigest(
                    commercialTermsDigest,
                    nameof(commercialTermsDigest)),
            AdmissionEntitlementDigest =
                FairReturnSupplyUnit.NormalizeDigest(
                    admissionEntitlementDigest,
                    nameof(admissionEntitlementDigest)),
            GrossMinorUnits = grossMinorUnits,
            RefundFundingModeId =
                refundFundingModeId,
            Priority = priority,
            EnqueuedAt = enqueuedAt,
            OpenRegistrationOrderLineId =
                registrationOrderLineId,
            StatusId =
                (int)EventWaitlistEntryStatus.Queued,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = enqueuedAt,
        };
    }

    public bool IsCommerciallyEquivalentTo(
        FairReturnSupplyUnit supply)
    {
        ArgumentNullException.ThrowIfNull(supply);
        return TenantId == supply.TenantId
            && EventId == supply.EventId
            && EventTicketTypeId ==
                supply.EventTicketTypeId
            && TicketCatalogVersionId ==
                supply.TicketCatalogVersionId
            && PurchasePolicySnapshotId ==
                supply.PurchasePolicySnapshotId
            && string.Equals(
                CurrencyCode,
                supply.CurrencyCode,
                StringComparison.Ordinal)
            && string.Equals(
                CommercialTermsDigest,
                supply.CommercialTermsDigest,
                StringComparison.Ordinal)
            && string.Equals(
                AdmissionEntitlementDigest,
                supply.AdmissionEntitlementDigest,
                StringComparison.Ordinal)
            && GrossMinorUnits ==
                supply.GrossMinorUnits
            && RefundFundingModeId ==
                supply.RefundFundingModeId;
    }

    public void MarkOffered(DateTime offeredAtUtc)
    {
        if (StatusId !=
            (int)EventWaitlistEntryStatus.Queued)
        {
            throw new InvalidOperationException(
                "Only queued entries can be offered.");
        }
        Transition(
            EventWaitlistEntryStatus.Offered,
            offeredAtUtc);
    }

    public void Requeue(DateTime requeuedAtUtc)
    {
        if (StatusId !=
            (int)EventWaitlistEntryStatus.Offered)
        {
            throw new InvalidOperationException(
                "Only offered entries can be requeued.");
        }
        Transition(
            EventWaitlistEntryStatus.Queued,
            requeuedAtUtc);
    }

    public void Convert(DateTime convertedAtUtc)
    {
        if (StatusId !=
            (int)EventWaitlistEntryStatus.Offered)
        {
            throw new InvalidOperationException(
                "Only offered entries can convert.");
        }
        Transition(
            EventWaitlistEntryStatus.Converted,
            convertedAtUtc,
            close: true);
    }

    public void Withdraw(DateTime withdrawnAtUtc)
    {
        if (StatusId ==
            (int)EventWaitlistEntryStatus.Withdrawn)
        {
            return;
        }
        if (StatusId !=
            (int)EventWaitlistEntryStatus.Queued)
        {
            throw new InvalidOperationException(
                "Only queued entries can be withdrawn.");
        }
        Transition(
            EventWaitlistEntryStatus.Withdrawn,
            withdrawnAtUtc,
            close: true);
    }

    private void Transition(
        EventWaitlistEntryStatus status,
        DateTime atUtc,
        bool close = false)
    {
        DateTime at =
            FairReturnSupplyPolicy.RequireUtc(
                atUtc,
                nameof(atUtc));
        StatusId = (int)status;
        if (close)
        {
            OpenRegistrationOrderLineId = null;
        }
        UpdatedAt = at;
        ConcurrencyStamp = Guid.CreateVersion7();
    }
}

public sealed class EventWaitlistOffer :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private Guid _tenantId;
    private EventWaitlistOffer()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(
            ref _tenantId,
            value,
            nameof(EventWaitlistOffer));
    }
    public Guid EventId { get; private set; }
    public Guid EventWaitlistEntryId { get; private set; }
    public Guid FairReturnSupplyUnitId { get; private set; }
    public Guid FairReturnSourceBindingId { get; private set; }
    public Guid ExistingCapacityHoldId { get; private set; }
    public Guid? OpenEventWaitlistEntryId { get; private set; }
    public DateTime OfferedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? FinalizedAt { get; private set; }
    public DateTime? ExpiredAt { get; private set; }
    public int StatusId { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static EventWaitlistOffer Create(
        Guid id,
        FairReturnSupplyPolicy policy,
        EventWaitlistEntry entry,
        FairReturnSupplyUnit supply,
        Guid sourceBindingId,
        Guid existingCapacityHoldId,
        DateTime offeredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(supply);
        FairReturnSupplyPolicy.RequireUuidV7(
            id,
            nameof(id));
        FairReturnSupplyPolicy.RequireUuidV7(
            sourceBindingId,
            nameof(sourceBindingId));
        FairReturnSupplyPolicy.RequireUuidV7(
            existingCapacityHoldId,
            nameof(existingCapacityHoldId));
        DateTime offeredAt =
            FairReturnSupplyPolicy.RequireUtc(
                offeredAtUtc,
                nameof(offeredAtUtc));
        if (!policy.IsEnabled
            || policy.TenantId != entry.TenantId
            || policy.TenantId != supply.TenantId
            || policy.EventId != entry.EventId
            || policy.EventId != supply.EventId
            || policy.EventTicketTypeId !=
                entry.EventTicketTypeId
            || policy.EventTicketTypeId !=
                supply.EventTicketTypeId
            || policy.TicketCatalogVersionId !=
                entry.TicketCatalogVersionId
            || policy.TicketCatalogVersionId !=
                supply.TicketCatalogVersionId
            || !entry.IsCommerciallyEquivalentTo(
                supply)
            || entry.StatusId !=
                (int)EventWaitlistEntryStatus.Queued
            || supply.StatusId !=
                (int)FairReturnSupplyStatus.Available)
        {
            throw new InvalidOperationException(
                "Waitlist offer authority is unavailable.");
        }
        entry.MarkOffered(offeredAt);
        supply.Bind(offeredAt);
        return new EventWaitlistOffer
        {
            Id = id,
            TenantId = policy.TenantId,
            EventId = policy.EventId,
            EventWaitlistEntryId = entry.Id,
            FairReturnSupplyUnitId = supply.Id,
            FairReturnSourceBindingId =
                sourceBindingId,
            ExistingCapacityHoldId =
                existingCapacityHoldId,
            OpenEventWaitlistEntryId = entry.Id,
            OfferedAt = offeredAt,
            ExpiresAt = offeredAt.AddMinutes(
                policy.OfferLifetimeMinutes),
            StatusId =
                (int)EventWaitlistOfferStatus.Active,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = offeredAt,
        };
    }

    public bool Expire(
        EventWaitlistEntry entry,
        FairReturnSupplyUnit supply,
        DateTime expiredAtUtc)
    {
        DateTime expiredAt =
            FairReturnSupplyPolicy.RequireUtc(
                expiredAtUtc,
                nameof(expiredAtUtc));
        if (StatusId !=
                (int)EventWaitlistOfferStatus.Active
            || expiredAt < ExpiresAt)
        {
            return false;
        }
        entry.Requeue(expiredAt);
        supply.Release(expiredAt);
        StatusId =
            (int)EventWaitlistOfferStatus.Expired;
        ExpiredAt = expiredAt;
        Close(expiredAt);
        return true;
    }

    public bool Finalize(
        EventWaitlistEntry entry,
        DateTime finalizedAtUtc)
    {
        if (StatusId ==
            (int)EventWaitlistOfferStatus.Finalized)
        {
            return false;
        }
        if (StatusId !=
            (int)EventWaitlistOfferStatus.Active)
        {
            throw new InvalidOperationException(
                "Only active offers can finalize.");
        }
        DateTime finalizedAt =
            FairReturnSupplyPolicy.RequireUtc(
                finalizedAtUtc,
                nameof(finalizedAtUtc));
        if (finalizedAt >= ExpiresAt)
        {
            throw new InvalidOperationException(
                "Expired offers cannot finalize.");
        }
        entry.Convert(finalizedAt);
        StatusId =
            (int)EventWaitlistOfferStatus.Finalized;
        FinalizedAt = finalizedAt;
        Close(finalizedAt);
        return true;
    }

    private void Close(DateTime at)
    {
        OpenEventWaitlistEntryId = null;
        UpdatedAt = at;
        ConcurrencyStamp = Guid.CreateVersion7();
    }
}

public sealed class FairReturnSourceBinding :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private Guid _tenantId;
    private FairReturnSourceBinding()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(
            ref _tenantId,
            value,
            nameof(FairReturnSourceBinding));
    }
    public Guid EventId { get; private set; }
    public Guid FairReturnSupplyUnitId { get; private set; }
    public Guid BuyerRegistrationOrderId { get; private set; }
    public Guid BuyerRegistrationOrderLineId { get; private set; }
    public Guid BuyerAccountUserId { get; private set; }
    public long UnitAmountMinor { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public string CommercialTermsDigest { get; private set; } = string.Empty;
    public string AdmissionEntitlementDigest { get; private set; } = string.Empty;
    public DateTime? PaymentDispatchClaimedAt { get; private set; }
    public DateTime? SourceSubstitutedAt { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static FairReturnSourceBinding Create(
        Guid id,
        FairReturnSupplyUnit supply,
        EventWaitlistEntry entry,
        DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(supply);
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsCommerciallyEquivalentTo(supply))
        {
            throw new InvalidOperationException(
                "Supply is not commercially equivalent.");
        }
        FairReturnSupplyPolicy.RequireUuidV7(id, nameof(id));
        FairReturnSupplyPolicy.RequireUuidV7(
            entry.RegistrationOrderId,
            nameof(entry.RegistrationOrderId));
        FairReturnSupplyPolicy.RequireUuidV7(
            entry.RegistrationOrderLineId,
            nameof(entry.RegistrationOrderLineId));
        FairReturnSupplyPolicy.RequireUuidV7(
            entry.BuyerAccountUserId,
            nameof(entry.BuyerAccountUserId));
        DateTime createdAt =
            FairReturnSupplyPolicy.RequireUtc(
                createdAtUtc,
                nameof(createdAtUtc));
        return new FairReturnSourceBinding
        {
            Id = id,
            TenantId = supply.TenantId,
            EventId = supply.EventId,
            FairReturnSupplyUnitId = supply.Id,
            BuyerRegistrationOrderId =
                entry.RegistrationOrderId,
            BuyerRegistrationOrderLineId =
                entry.RegistrationOrderLineId,
            BuyerAccountUserId =
                entry.BuyerAccountUserId,
            UnitAmountMinor =
                entry.GrossMinorUnits,
            CurrencyCode = entry.CurrencyCode,
            CommercialTermsDigest =
                entry.CommercialTermsDigest,
            AdmissionEntitlementDigest =
                entry.AdmissionEntitlementDigest,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = createdAt,
        };
    }

    public void SubstituteSource(
        FairReturnSupplyUnit current,
        FairReturnSupplyUnit replacement,
        DateTime substitutedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(replacement);
        if (PaymentDispatchClaimedAt.HasValue
            || current.Id !=
                FairReturnSupplyUnitId
            || !current.IsCommerciallyEquivalentTo(
                replacement)
            || replacement.StatusId !=
                (int)FairReturnSupplyStatus.Available)
        {
            throw new InvalidOperationException(
                "Source cannot be substituted.");
        }
        DateTime substitutedAt =
            FairReturnSupplyPolicy.RequireUtc(
                substitutedAtUtc,
                nameof(substitutedAtUtc));
        current.Withdraw(substitutedAt);
        replacement.Bind(substitutedAt);
        FairReturnSupplyUnitId = replacement.Id;
        SourceSubstitutedAt = substitutedAt;
        UpdatedAt = substitutedAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void ClaimPaymentDispatch(
        DateTime claimedAtUtc)
    {
        if (PaymentDispatchClaimedAt.HasValue)
        {
            return;
        }
        DateTime claimedAt =
            FairReturnSupplyPolicy.RequireUtc(
                claimedAtUtc,
                nameof(claimedAtUtc));
        PaymentDispatchClaimedAt = claimedAt;
        UpdatedAt = claimedAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }
}

public sealed class WaitlistProviderObservation :
    ITenantEntity,
    IAuditableEntity
{
    private Guid _tenantId;
    private WaitlistProviderObservation()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(
            ref _tenantId,
            value,
            nameof(WaitlistProviderObservation));
    }
    public Guid FairReturnSourceBindingId { get; private set; }
    public string ProviderCode { get; private set; } = string.Empty;
    public string ProviderObjectType { get; private set; } = string.Empty;
    public string ProviderObjectIdDigest { get; private set; } = string.Empty;
    public string ProviderObservationIdDigest { get; private set; } = string.Empty;
    public DateTime ObservedAt { get; private set; }
    public string StateCode { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static WaitlistProviderObservation Create(
        Guid id,
        FairReturnSourceBinding binding,
        string providerCode,
        string providerObjectType,
        string providerObjectIdDigest,
        string providerObservationIdDigest,
        DateTime observedAtUtc,
        string stateCode)
    {
        ArgumentNullException.ThrowIfNull(binding);
        FairReturnSupplyPolicy.RequireUuidV7(id, nameof(id));
        DateTime observedAt =
            FairReturnSupplyPolicy.RequireUtc(
                observedAtUtc,
                nameof(observedAtUtc));
        return new WaitlistProviderObservation
        {
            Id = id,
            TenantId = binding.TenantId,
            FairReturnSourceBindingId = binding.Id,
            ProviderCode = NormalizeStateCode(
                providerCode,
                nameof(providerCode)),
            ProviderObjectType = NormalizeStateCode(
                providerObjectType,
                nameof(providerObjectType)),
            ProviderObjectIdDigest =
                FairReturnSupplyUnit.NormalizeDigest(
                    providerObjectIdDigest,
                    nameof(providerObjectIdDigest)),
            ProviderObservationIdDigest =
                FairReturnSupplyUnit.NormalizeDigest(
                    providerObservationIdDigest,
                    nameof(providerObservationIdDigest)),
            ObservedAt = observedAt,
            StateCode = NormalizeStateCode(
                stateCode,
                nameof(stateCode)),
            CreatedAt = observedAt,
        };
    }

    public FairReturnOutcome ApplyIfNewer(
        string observationIdDigest,
        DateTime observedAtUtc,
        string stateCode)
    {
        DateTime observedAt =
            FairReturnSupplyPolicy.RequireUtc(
                observedAtUtc,
                nameof(observedAtUtc));
        string digest =
            FairReturnSupplyUnit.NormalizeDigest(
                observationIdDigest,
                nameof(observationIdDigest));
        if (observedAt <= ObservedAt)
        {
            return observedAt == ObservedAt
                && string.Equals(
                    digest,
                    ProviderObservationIdDigest,
                    StringComparison.Ordinal)
                    ? FairReturnOutcome.AlreadyApplied
                    : FairReturnOutcome.StaleObservation;
        }
        ProviderObservationIdDigest = digest;
        ObservedAt = observedAt;
        StateCode = NormalizeStateCode(
            stateCode,
            nameof(stateCode));
        UpdatedAt = observedAt;
        return FairReturnOutcome.ReplacementFinalized;
    }

    private static string NormalizeStateCode(
        string value,
        string parameterName)
    {
        string normalized =
            value?.Trim().ToUpperInvariant()
            ?? string.Empty;
        if (normalized.Length is < 1 or > 32
            || normalized.Any(character =>
                !(character is >= 'A' and <= 'Z'
                  or >= '0' and <= '9'
                  or '_' or '-')))
        {
            throw new ArgumentException(
                "State code is invalid.",
                parameterName);
        }
        return normalized;
    }
}

public sealed class WaitlistRefundIntent :
    ITenantEntity,
    IAuditableEntity
{
    private Guid _tenantId;
    private WaitlistRefundIntent()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(
            ref _tenantId,
            value,
            nameof(WaitlistRefundIntent));
    }
    public Guid FairReturnSourceBindingId { get; private set; }
    public Guid OriginalPaymentAllocationId { get; private set; }
    public Guid RefundAttemptId { get; private set; }
    public Guid StableOperationId { get; private set; }
    public string ProviderIdempotencyKey { get; private set; } = string.Empty;
    public Guid OutboxMessageId { get; private set; }
    public DateTime ReplacementPaymentSettledAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static WaitlistRefundIntent Create(
        Guid id,
        WaitlistPaymentIntent paymentIntent,
        Guid outboxMessageId,
        DateTime replacementPaymentSettledAtUtc)
    {
        ArgumentNullException.ThrowIfNull(paymentIntent);
        FairReturnSupplyPolicy.RequireUuidV7(id, nameof(id));
        FairReturnSupplyPolicy.RequireUuidV7(
            outboxMessageId,
            nameof(outboxMessageId));
        DateTime settledAt =
            FairReturnSupplyPolicy.RequireUtc(
                replacementPaymentSettledAtUtc,
                nameof(replacementPaymentSettledAtUtc));
        if (paymentIntent.ReplacementPaymentSettledAt
                != settledAt
            || id != paymentIntent.RefundIntentId)
        {
            throw new InvalidOperationException(
                "Replacement settlement authority is invalid.");
        }
        return new WaitlistRefundIntent
        {
            Id = id,
            TenantId = paymentIntent.TenantId,
            FairReturnSourceBindingId =
                paymentIntent.FairReturnSourceBindingId,
            OriginalPaymentAllocationId =
                paymentIntent.OriginalPaymentAllocationId,
            RefundAttemptId =
                paymentIntent.ReservedRefundAttemptId,
            StableOperationId =
                paymentIntent.StableOperationId,
            ProviderIdempotencyKey =
                paymentIntent.ProviderIdempotencyKey,
            OutboxMessageId = outboxMessageId,
            ReplacementPaymentSettledAt = settledAt,
            CreatedAt = settledAt,
        };
    }
}
