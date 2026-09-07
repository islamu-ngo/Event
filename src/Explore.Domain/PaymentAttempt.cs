using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class PaymentAttempt : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    public const string ActiveUniquenessSlotValue = "active";

    private PaymentAttempt()
    {
    }

    private PaymentAttempt(
        Guid id,
        Guid tenantId,
        Guid registrationOrderId,
        OrganizerPaymentRecipientSnapshot recipientSnapshot,
        string providerProfileCode,
        string providerApiRevision,
        string compositionRevision,
        long organizerAmountMinor,
        long platformFeeMinor,
        long platformContributionMinor,
        string providerIdempotencyKey,
        DateTime createdAt,
        DateTime? expiresAt)
    {
        Id = id;
        TenantId = tenantId;
        RegistrationOrderId = registrationOrderId;
        RecipientSnapshot = recipientSnapshot;
        ProviderCode = recipientSnapshot.ProviderCode;
        ProfileCode = providerProfileCode;
        ProviderApiRevision = providerApiRevision;
        CompositionRevision = compositionRevision;
        CurrencyCode = recipientSnapshot.CurrencyCode;
        OrganizerAmountMinor = organizerAmountMinor;
        PlatformFeeMinor = platformFeeMinor;
        PlatformContributionMinor = platformContributionMinor;
        TotalMinor = MinorUnitMath.Add(organizerAmountMinor, platformContributionMinor);
        ProviderIdempotencyKey = providerIdempotencyKey;
        ActiveScopeKey = CreateActiveScopeKey(tenantId, registrationOrderId);
        ActiveUniquenessSlot = ActiveUniquenessSlotValue;
        PaymentAttemptStatusId = (int)PaymentAttemptStatusEnum.Created;
        AuthoritativeStatusFloorId = (int)PaymentAttemptStatusEnum.Created;
        CreatedAt = createdAt;
        CampaignCursor = createdAt.Ticks;
        LastStatusObservedAt = createdAt;
        ExpiresAt = expiresAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; set; }

    public Guid RegistrationOrderId { get; private set; }

    public Guid? PaidOrderAcceptanceSnapshotId { get; private set; }

    public PaidOrderAcceptanceSnapshot? AcceptanceSnapshot { get; private set; }

    public OrganizerPaymentRecipientSnapshot RecipientSnapshot { get; private set; } = null!;

    public string ProviderCode { get; private set; } = string.Empty;

    public string ProfileCode { get; private set; } = string.Empty;

    public string ProviderApiRevision { get; private set; } = string.Empty;

    public string CompositionRevision { get; private set; } = string.Empty;

    public string CurrencyCode { get; private set; } = string.Empty;

    public long OrganizerAmountMinor { get; private set; }

    public long PlatformFeeMinor { get; private set; }

    public long PlatformContributionMinor { get; private set; }

    public long TotalMinor { get; private set; }

    public string ProviderIdempotencyKey { get; private set; } = string.Empty;

    public string ActiveScopeKey { get; private set; } = string.Empty;

    public string ActiveUniquenessSlot { get; private set; } = string.Empty;

    public string? ProviderCheckoutSessionId { get; private set; }

    public string? ProviderPaymentId { get; private set; }

    public int PaymentAttemptStatusId { get; private set; }

    public PaymentAttemptStatus? PaymentAttemptStatus { get; private set; }

    public int AuthoritativeStatusFloorId { get; private set; }

    public DateTime? ExpiresAt { get; private set; }

    public DateTime LastStatusObservedAt { get; private set; }

    public string? LastProviderRequestId { get; private set; }

    public DateTime? DispatchPendingAt { get; private set; }

    public DateTime? RequiresActionAt { get; private set; }

    public DateTime? ProcessingAt { get; private set; }

    public DateTime? SucceededAt { get; private set; }

    public DateTime? FailedAt { get; private set; }

    public DateTime? CancelledAt { get; private set; }

    public DateTime? UnknownAt { get; private set; }

    public DateTime CreatedAt { get; set; }

    public long CampaignCursor { get; private set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public Guid ConcurrencyStamp { get; set; }

    public static PaymentAttempt Create(
        Guid id,
        Guid tenantId,
        Guid registrationOrderId,
        OrganizerPaymentRecipientSnapshot recipientSnapshot,
        string providerProfileCode,
        string providerApiRevision,
        string compositionRevision,
        Money organizerAmount,
        Money platformFee,
        Money platformContribution,
        string providerIdempotencyKey,
        DateTime createdAt,
        DateTime? expiresAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || registrationOrderId == Guid.Empty)
        {
            throw new ArgumentException("Payment attempt identities are required.");
        }

        ArgumentNullException.ThrowIfNull(recipientSnapshot);
        ArgumentNullException.ThrowIfNull(organizerAmount);
        ArgumentNullException.ThrowIfNull(platformFee);
        ArgumentNullException.ThrowIfNull(platformContribution);
        if (recipientSnapshot.TenantId != tenantId)
        {
            throw new ArgumentException("Recipient snapshot must match the payment attempt tenant.", nameof(recipientSnapshot));
        }
        EnsureRecipientCurrency(recipientSnapshot, organizerAmount, platformFee, platformContribution);

        string normalizedProfileCode = NormalizeRequiredText(providerProfileCode, nameof(providerProfileCode), 80, preserveCase: true);
        if (!string.Equals(normalizedProfileCode, recipientSnapshot.ProfileCode, StringComparison.Ordinal))
        {
            throw new ArgumentException("Provider profile must match the immutable recipient snapshot.", nameof(providerProfileCode));
        }

        string normalizedApiRevision = NormalizeRequiredText(providerApiRevision, nameof(providerApiRevision), 80, preserveCase: true);
        string normalizedCompositionRevision = NormalizeRequiredText(compositionRevision, nameof(compositionRevision), 80, preserveCase: true);
        string normalizedIdempotencyKey = NormalizeRequiredText(providerIdempotencyKey, nameof(providerIdempotencyKey), 160, preserveCase: true);
        DateTime timestamp = OrganizerPaymentProviderConnection.EnsureUtc(createdAt, nameof(createdAt));
        DateTime? expiry = expiresAt.HasValue ? OrganizerPaymentProviderConnection.EnsureUtc(expiresAt.Value, nameof(expiresAt)) : null;
        if (expiry.HasValue && expiry.Value <= timestamp)
        {
            throw new ArgumentException("Payment attempt expiry must be after creation.", nameof(expiresAt));
        }

        long organizerAmountMinor = organizerAmount.MinorUnits;
        long platformFeeMinor = platformFee.MinorUnits;
        long platformContributionMinor = platformContribution.MinorUnits;
        EnsureMoneyComposition(organizerAmountMinor, platformFeeMinor, platformContributionMinor);
        return new PaymentAttempt(
            id,
            tenantId,
            registrationOrderId,
            recipientSnapshot,
            normalizedProfileCode,
            normalizedApiRevision,
            normalizedCompositionRevision,
            organizerAmountMinor,
            platformFeeMinor,
            platformContributionMinor,
            normalizedIdempotencyKey,
            timestamp,
            expiry);
    }

    public void AssignCampaignCursor(long cursor)
    {
        if (cursor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cursor));
        }
        if (CampaignCursor != CreatedAt.Ticks && CampaignCursor != cursor)
        {
            throw new InvalidOperationException("Payment campaign cursor is immutable after persistence assignment.");
        }

        CampaignCursor = cursor;
    }

    public void AttachAcceptance(PaidOrderAcceptanceSnapshot acceptance)
    {
        ArgumentNullException.ThrowIfNull(acceptance);
        if (PaidOrderAcceptanceSnapshotId.HasValue)
        {
            if (PaidOrderAcceptanceSnapshotId == acceptance.Id)
            {
                return;
            }

            throw new InvalidOperationException("Payment attempt acceptance evidence is immutable.");
        }

        if (acceptance.TenantId != TenantId || acceptance.RegistrationOrderId != RegistrationOrderId ||
            !string.Equals(acceptance.CompositionRevision, CompositionRevision, StringComparison.Ordinal) ||
            !string.Equals(acceptance.CurrencyCode, CurrencyCode, StringComparison.Ordinal) ||
            acceptance.OrganizerAmountMinor != OrganizerAmountMinor || acceptance.PlatformFeeMinor != PlatformFeeMinor ||
            acceptance.PlatformContributionMinor != PlatformContributionMinor || acceptance.TotalMinor != TotalMinor ||
            !string.Equals(acceptance.ProviderCode, ProviderCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(acceptance.ProviderProfileCode, ProfileCode, StringComparison.Ordinal) ||
            acceptance.OrganizerPaymentProviderConnectionId != RecipientSnapshot.OrganizerPaymentProviderConnectionId ||
            !string.Equals(acceptance.ConnectPlatformId, RecipientSnapshot.ConnectPlatformId, StringComparison.Ordinal) ||
            !string.Equals(acceptance.ExternalAccountId, RecipientSnapshot.ExternalAccountId, StringComparison.Ordinal) ||
            !string.Equals(acceptance.MerchantCountryCode, RecipientSnapshot.MerchantCountryCode, StringComparison.Ordinal))
        {
            throw new ArgumentException("Acceptance evidence must match the exact payment attempt facts.", nameof(acceptance));
        }

        PaidOrderAcceptanceSnapshotId = acceptance.Id;
        AcceptanceSnapshot = acceptance;
    }

    public bool HasImmutableAcceptance => PaidOrderAcceptanceSnapshotId.HasValue && AcceptanceSnapshot is not null;

    public bool MarkDispatchPending(DateTime observedAt, string? providerRequestId)
    {
        DateTime timestamp = NormalizeObservedAt(observedAt);
        return Advance(
            PaymentAttemptStatusEnum.DispatchPending,
            timestamp,
            providerRequestId,
            () => DispatchPendingAt ??= timestamp);
    }

    public bool MarkRequiresAction(string providerCheckoutSessionId, DateTime observedAt, string? providerRequestId)
    {
        DateTime timestamp = NormalizeObservedAt(observedAt);
        if (IsStale(timestamp))
        {
            return false;
        }

        EnsureCheckoutSessionMatches(providerCheckoutSessionId);
        return Advance(
            PaymentAttemptStatusEnum.RequiresAction,
            timestamp,
            providerRequestId,
            () =>
            {
                BindCheckoutSession(providerCheckoutSessionId);
                RequiresActionAt ??= timestamp;
            });
    }

    public bool MarkProcessing(string providerPaymentId, DateTime observedAt, string? providerRequestId) => MarkProcessing(
        providerCheckoutSessionId: null,
        providerPaymentId,
        observedAt,
        providerRequestId);

    public bool MarkProcessing(string? providerCheckoutSessionId, string providerPaymentId, DateTime observedAt, string? providerRequestId)
    {
        DateTime timestamp = NormalizeObservedAt(observedAt);
        if (IsStale(timestamp))
        {
            return false;
        }

        if (providerCheckoutSessionId is not null)
        {
            EnsureCheckoutSessionMatches(providerCheckoutSessionId);
        }

        EnsureProviderPaymentMatches(providerPaymentId);
        return Advance(
            PaymentAttemptStatusEnum.Processing,
            timestamp,
            providerRequestId,
            () =>
            {
                if (providerCheckoutSessionId is not null)
                {
                    BindCheckoutSession(providerCheckoutSessionId);
                }

                BindProviderPayment(providerPaymentId);
                ProcessingAt ??= timestamp;
            });
    }

    public bool MarkSucceeded(string providerPaymentId, DateTime observedAt, string? providerRequestId)
    {
        DateTime timestamp = NormalizeObservedAt(observedAt);
        if (IsStale(timestamp))
        {
            return false;
        }

        EnsureProviderPaymentMatches(providerPaymentId);
        return Advance(
            PaymentAttemptStatusEnum.Succeeded,
            timestamp,
            providerRequestId,
            () =>
            {
                BindProviderPayment(providerPaymentId);
                SucceededAt ??= timestamp;
            });
    }

    public bool MarkSucceededFromCheckout(string providerCheckoutSessionId, string providerPaymentId, DateTime observedAt, string? providerRequestId)
        => MarkTerminalFromCheckout(
            PaymentAttemptStatusEnum.Succeeded,
            providerCheckoutSessionId,
            providerPaymentId,
            observedAt,
            providerRequestId,
            timestamp => SucceededAt ??= timestamp);

    public bool MarkFailed(string providerPaymentId, DateTime observedAt, string? providerRequestId)
    {
        DateTime timestamp = NormalizeObservedAt(observedAt);
        if (IsStale(timestamp))
        {
            return false;
        }

        EnsureProviderPaymentMatches(providerPaymentId);
        return Advance(
            PaymentAttemptStatusEnum.Failed,
            timestamp,
            providerRequestId,
            () =>
            {
                BindProviderPayment(providerPaymentId);
                FailedAt ??= timestamp;
            });
    }

    public bool MarkFailedFromCheckout(string providerCheckoutSessionId, string providerPaymentId, DateTime observedAt, string? providerRequestId)
        => MarkTerminalFromCheckout(
            PaymentAttemptStatusEnum.Failed,
            providerCheckoutSessionId,
            providerPaymentId,
            observedAt,
            providerRequestId,
            timestamp => FailedAt ??= timestamp);

    public bool MarkCancelled(DateTime observedAt, string? providerRequestId)
    {
        DateTime timestamp = NormalizeObservedAt(observedAt);
        return Advance(
            PaymentAttemptStatusEnum.Cancelled,
            timestamp,
            providerRequestId,
            () => CancelledAt ??= timestamp);
    }

    public bool MarkCancelledFromCheckout(string providerCheckoutSessionId, DateTime observedAt, string? providerRequestId)
    {
        DateTime timestamp = NormalizeObservedAt(observedAt);
        if (IsStale(timestamp))
        {
            return false;
        }

        EnsureCheckoutSessionMatches(providerCheckoutSessionId);
        return Advance(
            PaymentAttemptStatusEnum.Cancelled,
            timestamp,
            providerRequestId,
            () =>
            {
                BindCheckoutSession(providerCheckoutSessionId);
                CancelledAt ??= timestamp;
            });
    }

    public bool MarkUnknown(DateTime observedAt, string? providerRequestId)
    {
        DateTime timestamp = NormalizeObservedAt(observedAt);
        return Advance(
            PaymentAttemptStatusEnum.Unknown,
            timestamp,
            providerRequestId,
            () => UnknownAt ??= timestamp);
    }

    public bool MarkDispatchFailed(DateTime observedAt, string? providerRequestId)
    {
        DateTime timestamp = NormalizeObservedAt(observedAt);
        return Advance(
            PaymentAttemptStatusEnum.Failed,
            timestamp,
            providerRequestId,
            () => FailedAt ??= timestamp);
    }

    public bool TryReleaseActiveSlot(DateTime releasedAt)
    {
        DateTime timestamp = NormalizeObservedAt(releasedAt);
        if (ActiveUniquenessSlot != ActiveUniquenessSlotValue)
        {
            return false;
        }

        PaymentAttemptStatusEnum status = (PaymentAttemptStatusEnum)PaymentAttemptStatusId;
        if (status is not (PaymentAttemptStatusEnum.Failed or PaymentAttemptStatusEnum.Cancelled))
        {
            return false;
        }

        ActiveUniquenessSlot = $"{status.ToString().ToLowerInvariant()}:{Id:N}";
        UpdatedAt = timestamp;
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }

    public bool ExtendPaymentCutoff(DateTime cutoff, DateTime changedAt)
    {
        DateTime utcCutoff = NormalizeObservedAt(cutoff);
        DateTime utcChangedAt = NormalizeObservedAt(changedAt);
        if (ProviderCheckoutSessionId is not null || ProviderPaymentId is not null ||
            IsTerminal((PaymentAttemptStatusEnum)PaymentAttemptStatusId) ||
            ExpiresAt is not { } current || utcCutoff <= current)
        {
            return false;
        }

        ExpiresAt = utcCutoff;
        UpdatedAt = utcChangedAt;
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }

    private bool MarkTerminalFromCheckout(
        PaymentAttemptStatusEnum terminalStatus,
        string providerCheckoutSessionId,
        string providerPaymentId,
        DateTime observedAt,
        string? providerRequestId,
        Action<DateTime> markTerminalAt)
    {
        DateTime timestamp = NormalizeObservedAt(observedAt);
        if (IsStale(timestamp))
        {
            return false;
        }

        EnsureCheckoutSessionMatches(providerCheckoutSessionId);
        EnsureProviderPaymentMatches(providerPaymentId);
        return Advance(
            terminalStatus,
            timestamp,
            providerRequestId,
            () =>
            {
                BindCheckoutSession(providerCheckoutSessionId);
                BindProviderPayment(providerPaymentId);
                markTerminalAt(timestamp);
            });
    }

    private bool Advance(PaymentAttemptStatusEnum desiredStatus, DateTime timestamp, string? providerRequestId, Action applyEvidence)
    {
        if (!Enum.IsDefined(desiredStatus) || desiredStatus == PaymentAttemptStatusEnum.Created)
        {
            throw new ArgumentException("Payment attempt status transition must be explicit.", nameof(desiredStatus));
        }

        PaymentAttemptStatusEnum currentStatus = (PaymentAttemptStatusEnum)PaymentAttemptStatusId;
        if (timestamp <= LastStatusObservedAt)
        {
            return false;
        }

        if (IsTerminal(currentStatus))
        {
            if (currentStatus == desiredStatus)
            {
                return false;
            }

            throw new InvalidOperationException("Terminal payment attempt state cannot regress or change outcome.");
        }

        if (currentStatus == desiredStatus || (desiredStatus != PaymentAttemptStatusEnum.Unknown && Rank(desiredStatus) < Rank((PaymentAttemptStatusEnum)AuthoritativeStatusFloorId)))
        {
            return false;
        }

        applyEvidence();
        PaymentAttemptStatusId = (int)desiredStatus;
        if (desiredStatus != PaymentAttemptStatusEnum.Unknown)
        {
            AuthoritativeStatusFloorId = (int)desiredStatus;
        }

        LastStatusObservedAt = timestamp;
        LastProviderRequestId = NormalizeOptionalText(providerRequestId, nameof(providerRequestId), 120);
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }

    private static DateTime NormalizeObservedAt(DateTime observedAt) => OrganizerPaymentProviderConnection.EnsureUtc(observedAt, nameof(observedAt));

    private bool IsStale(DateTime observedAt) => observedAt <= LastStatusObservedAt;

    private void BindCheckoutSession(string providerCheckoutSessionId)
    {
        string normalized = OrganizerPaymentProviderConnection.NormalizeProviderIdentity(providerCheckoutSessionId, nameof(providerCheckoutSessionId), 200, preserveCase: true);
        if (ProviderCheckoutSessionId is null)
        {
            ProviderCheckoutSessionId = normalized;
            return;
        }

        if (!string.Equals(ProviderCheckoutSessionId, normalized, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Provider checkout session identity is already bound.");
        }
    }

    private void EnsureCheckoutSessionMatches(string providerCheckoutSessionId)
    {
        string normalized = OrganizerPaymentProviderConnection.NormalizeProviderIdentity(providerCheckoutSessionId, nameof(providerCheckoutSessionId), 200, preserveCase: true);
        if (ProviderCheckoutSessionId is not null && !string.Equals(ProviderCheckoutSessionId, normalized, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Provider checkout session identity is already bound.");
        }
    }

    private void BindProviderPayment(string providerPaymentId)
    {
        string normalized = OrganizerPaymentProviderConnection.NormalizeProviderIdentity(providerPaymentId, nameof(providerPaymentId), 200, preserveCase: true);
        if (ProviderPaymentId is null)
        {
            ProviderPaymentId = normalized;
            return;
        }

        if (!string.Equals(ProviderPaymentId, normalized, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Provider payment identity is already bound.");
        }
    }

    private void EnsureProviderPaymentMatches(string providerPaymentId)
    {
        string normalized = OrganizerPaymentProviderConnection.NormalizeProviderIdentity(providerPaymentId, nameof(providerPaymentId), 200, preserveCase: true);
        if (ProviderPaymentId is not null && !string.Equals(ProviderPaymentId, normalized, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Provider payment identity is already bound.");
        }
    }

    private static void EnsureRecipientCurrency(
        OrganizerPaymentRecipientSnapshot recipientSnapshot,
        params Money[] amounts)
    {
        if (amounts.Any(amount => !string.Equals(
                amount.CurrencyCode,
                recipientSnapshot.CurrencyCode,
                StringComparison.Ordinal)))
        {
            throw new ArgumentException("Payment amounts must use the recipient currency.", nameof(amounts));
        }
    }

    private static void EnsureMoneyComposition(long organizerAmountMinor, long platformFeeMinor, long platformContributionMinor)
    {
        if (organizerAmountMinor < 0 || platformFeeMinor < 0 || platformContributionMinor < 0 || platformFeeMinor > organizerAmountMinor)
        {
            throw new ArgumentException("Payment amount composition is invalid.");
        }

        _ = MinorUnitMath.Add(organizerAmountMinor, platformContributionMinor);
    }

    private static bool IsTerminal(PaymentAttemptStatusEnum status) => status is PaymentAttemptStatusEnum.Succeeded or PaymentAttemptStatusEnum.Failed or PaymentAttemptStatusEnum.Cancelled;

    private static int Rank(PaymentAttemptStatusEnum status) => status switch
    {
        PaymentAttemptStatusEnum.Created => 0,
        PaymentAttemptStatusEnum.DispatchPending => 1,
        PaymentAttemptStatusEnum.RequiresAction => 2,
        PaymentAttemptStatusEnum.Processing => 3,
        PaymentAttemptStatusEnum.Unknown => 4,
        PaymentAttemptStatusEnum.Succeeded or PaymentAttemptStatusEnum.Failed or PaymentAttemptStatusEnum.Cancelled => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static string NormalizeRequiredText(string value, string parameterName, int maxLength, bool preserveCase)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is 0 || normalized.Length > maxLength || normalized.Any(char.IsControl))
        {
            throw new ArgumentException($"Value must be non-blank and at most {maxLength} characters.", parameterName);
        }

        return preserveCase ? normalized : normalized.ToLowerInvariant();
    }

    public static string CreateActiveScopeKey(Guid tenantId, Guid registrationOrderId)
    {
        if (tenantId == Guid.Empty || registrationOrderId == Guid.Empty)
        {
            throw new ArgumentException("Payment attempt active scope identities are required.");
        }

        return string.Join('|', tenantId.ToString("N"), registrationOrderId.ToString("N"));
    }

    private static string? NormalizeOptionalText(string? value, string parameterName, int maxLength)
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
