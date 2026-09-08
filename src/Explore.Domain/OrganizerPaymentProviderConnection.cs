using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class OrganizerPaymentProviderConnection : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private const string ActiveUniquenessSlotValue = "active";

    private readonly List<OrganizerPaymentProviderConnectionSupportedCurrency> _supportedCurrencyCodes = [];

    private OrganizerPaymentProviderConnection()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid OrganizerActorId { get; private set; }
    public string ProviderCode { get; private set; } = string.Empty;
    public string ConnectPlatformId { get; private set; } = string.Empty;
    public string ExternalAccountId { get; private set; } = string.Empty;
    public string ActiveScopeKey { get; private set; } = string.Empty;
    public string ActiveUniquenessSlot { get; private set; } = string.Empty;
    public int StatusId { get; private set; }
    public string? MerchantCountryCode { get; private set; }
    public int ChargeCapabilityStateId { get; private set; } = (int)ChargeCapabilityState.Unknown;
    public int RequirementsStateId { get; private set; } = (int)ProviderRequirementsState.Unknown;
    public IReadOnlyList<string> SupportedCurrencyCodes => _supportedCurrencyCodes
        .OrderBy(row => row.Ordinal)
        .Select(row => row.CurrencyCode)
        .ToArray();

    private IReadOnlyCollection<OrganizerPaymentProviderConnectionSupportedCurrency> SupportedCurrencyRows => _supportedCurrencyCodes;
    public DateTime? LastReadinessObservedAt { get; private set; }
    public string? LastReadinessEvidenceRevision { get; private set; }
    public Guid? ReplacesConnectionId { get; private set; }
    public Guid? ReplacedByConnectionId { get; private set; }
    public DateTime? ReplacedAt { get; private set; }
    public DateTime? DisabledAt { get; private set; }
    public string? DisabledReasonCode { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public static OrganizerPaymentProviderConnection Create(
        Guid id,
        Guid tenantId,
        Guid organizerActorId,
        string providerCode,
        string connectPlatformId,
        string externalAccountId,
        DateTime createdAt) => CreateInternal(
            id,
            tenantId,
            organizerActorId,
            providerCode,
            connectPlatformId,
            externalAccountId,
            replacesConnectionId: null,
            createdAt);

    public void ApplyReadiness(OrganizerPaymentProviderReadinessObservation observation)
    {
        EnsureCanAcceptReadiness();
        ArgumentNullException.ThrowIfNull(observation);
        if (LastReadinessObservedAt is { } observedAt && observation.ObservedAt <= observedAt)
        {
            throw new InvalidOperationException("Readiness observations must be newer than the current evidence.");
        }

        MerchantCountryCode = observation.MerchantCountryCode;
        ChargeCapabilityStateId = (int)observation.ChargeCapabilityState;
        RequirementsStateId = (int)observation.RequirementsState;
        _supportedCurrencyCodes.Clear();
        _supportedCurrencyCodes.AddRange(observation.SupportedCurrencyCodes.Select((currencyCode, index) =>
            OrganizerPaymentProviderConnectionSupportedCurrency.Create(TenantId, Id, index, currencyCode)));
        LastReadinessObservedAt = observation.ObservedAt;
        LastReadinessEvidenceRevision = observation.EvidenceRevision;
        StatusId = observation.IsReady
            ? (int)OrganizerPaymentProviderConnectionStatusEnum.Ready
            : (int)OrganizerPaymentProviderConnectionStatusEnum.Restricted;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public OrganizerPaymentProviderConnection ReplaceWith(Guid replacementConnectionId, string externalAccountId, DateTime replacedAt)
    {
        EnsureActiveForReplacement();
        string normalizedExternalAccountId = NormalizeRequiredText(externalAccountId, nameof(externalAccountId), 200, preserveCase: true);
        if (replacementConnectionId == Guid.Empty || replacementConnectionId == Id)
        {
            throw new ArgumentException("Replacement connection identity is required.", nameof(replacementConnectionId));
        }

        if (string.Equals(normalizedExternalAccountId, ExternalAccountId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Replacement must use a new external account identity.", nameof(externalAccountId));
        }

        DateTime timestamp = EnsureUtc(replacedAt, nameof(replacedAt));
        if (timestamp < CreatedAt)
        {
            throw new ArgumentException("Replacement cannot predate connection creation.", nameof(replacedAt));
        }

        OrganizerPaymentProviderConnection replacement = CreateInternal(
            replacementConnectionId,
            TenantId,
            OrganizerActorId,
            ProviderCode,
            ConnectPlatformId,
            normalizedExternalAccountId,
            Id,
            timestamp);
        MarkReplaced(timestamp);
        return replacement;
    }

    public void MarkReplacedBy(Guid replacementConnectionId)
    {
        if (StatusId != (int)OrganizerPaymentProviderConnectionStatusEnum.Replaced)
        {
            throw new InvalidOperationException("Only replaced organizer payment connections can record replacement lineage.");
        }

        if (replacementConnectionId == Guid.Empty || replacementConnectionId == Id)
        {
            throw new ArgumentException("Replacement connection identity is required.", nameof(replacementConnectionId));
        }

        if (ReplacedByConnectionId == replacementConnectionId)
        {
            return;
        }

        if (ReplacedByConnectionId is not null)
        {
            throw new InvalidOperationException("Replacement lineage is already recorded.");
        }

        ReplacedByConnectionId = replacementConnectionId;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void Disable(string reasonCode, DateTime disabledAt)
    {
        EnsureNotTerminal();
        DisabledReasonCode = NormalizeReasonCode(reasonCode);
        DisabledAt = EnsureUtc(disabledAt, nameof(disabledAt));
        StatusId = (int)OrganizerPaymentProviderConnectionStatusEnum.Disabled;
        ActiveUniquenessSlot = CreateTerminalUniquenessSlot(nameof(OrganizerPaymentProviderConnectionStatusEnum.Disabled));
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public OrganizerPaymentRecipientSnapshot CreateRecipientSnapshot(
        string currencyCode,
        Guid instancePolicyVersionId,
        Guid? tenantPolicyVersionId,
        DateTime snapshottedAt)
    {
        if (StatusId != (int)OrganizerPaymentProviderConnectionStatusEnum.Ready)
        {
            throw new InvalidOperationException("Only ready organizer payment connections can create recipient snapshots.");
        }

        if (instancePolicyVersionId == Guid.Empty || tenantPolicyVersionId == Guid.Empty)
        {
            throw new ArgumentException("Policy version identity is required.");
        }

        string normalizedCurrencyCode = NormalizeCurrencyCode(currencyCode);
        if (!_supportedCurrencyCodes.Any(row => row.CurrencyCode == normalizedCurrencyCode))
        {
            throw new ArgumentException("Snapshot currency must be supported by the ready connection.", nameof(currencyCode));
        }

        DateTime timestamp = EnsureUtc(snapshottedAt, nameof(snapshottedAt));
        if (timestamp < CreatedAt || timestamp < LastReadinessObservedAt)
        {
            throw new ArgumentException("Recipient snapshot timestamp cannot predate connection creation or readiness evidence.", nameof(snapshottedAt));
        }

        return OrganizerPaymentRecipientSnapshot.Create(
            TenantId,
            OrganizerActorId,
            Id,
            ProviderCode,
            ConnectPlatformId,
            ExternalAccountId,
            MerchantCountryCode ?? throw new InvalidOperationException("Ready connections require a merchant country."),
            normalizedCurrencyCode,
            instancePolicyVersionId,
            tenantPolicyVersionId,
            timestamp);
    }

    public OrganizerPaymentRecipientSnapshotResult TryCreateRecipientSnapshot(
        string currencyCode,
        Guid instancePolicyVersionId,
        Guid? tenantPolicyVersionId,
        DateTime snapshottedAt,
        TimeSpan maximumEvidenceAge)
    {
        DateTime timestamp = EnsureUtc(snapshottedAt, nameof(snapshottedAt));
        if (maximumEvidenceAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEvidenceAge));
        }

        OrganizerPaymentProviderConnectionStatusEnum status = (OrganizerPaymentProviderConnectionStatusEnum)StatusId;
        string? statusFailure = status switch
        {
            OrganizerPaymentProviderConnectionStatusEnum.PendingOnboarding => "payment_connection_pending",
            OrganizerPaymentProviderConnectionStatusEnum.Restricted => "payment_connection_restricted",
            OrganizerPaymentProviderConnectionStatusEnum.Disabled => "payment_connection_disabled",
            OrganizerPaymentProviderConnectionStatusEnum.Replaced => "payment_connection_replaced",
            OrganizerPaymentProviderConnectionStatusEnum.Ready => null,
            _ => "payment_connection_unavailable"
        };
        if (statusFailure is not null)
        {
            return OrganizerPaymentRecipientSnapshotResult.Failure(statusFailure);
        }

        string normalizedCurrency;
        try
        {
            normalizedCurrency = NormalizeCurrencyCode(currencyCode);
        }
        catch (ArgumentException)
        {
            return OrganizerPaymentRecipientSnapshotResult.Failure("payment_currency_unsupported");
        }

        if (!_supportedCurrencyCodes.Any(row => row.CurrencyCode == normalizedCurrency))
        {
            return OrganizerPaymentRecipientSnapshotResult.Failure("payment_currency_unsupported");
        }

        if (LastReadinessObservedAt is not { } observedAt || timestamp - observedAt > maximumEvidenceAge)
        {
            return OrganizerPaymentRecipientSnapshotResult.Failure("payment_readiness_stale");
        }

        if (instancePolicyVersionId == Guid.Empty || tenantPolicyVersionId == Guid.Empty || MerchantCountryCode is null)
        {
            return OrganizerPaymentRecipientSnapshotResult.Failure("payment_policy_unavailable");
        }

        return OrganizerPaymentRecipientSnapshotResult.Succeeded(OrganizerPaymentRecipientSnapshot.Create(
            TenantId,
            OrganizerActorId,
            Id,
            ProviderCode,
            ConnectPlatformId,
            ExternalAccountId,
            MerchantCountryCode,
            normalizedCurrency,
            instancePolicyVersionId,
            tenantPolicyVersionId,
            timestamp));
    }

    internal static string NormalizeProviderCode(string providerCode)
    {
        string normalized = NormalizeRequiredText(providerCode, nameof(providerCode), 40, preserveCase: false).ToLowerInvariant();
        return normalized == "stripe"
            ? normalized
            : throw new ArgumentException("Only the stable stripe provider code is supported.", nameof(providerCode));
    }

    internal static string NormalizeCurrencyCode(string currencyCode)
    {
        CurrencyMetadata currency = CurrencyMetadata.Get(currencyCode);
        if (currency.IsNoCurrency)
        {
            throw new ArgumentException("Organizer payment connections require monetary currencies.", nameof(currencyCode));
        }

        return currency.Code;
    }

    internal static DateTime EnsureUtc(DateTime value, string parameterName) =>
        value != default && value.Kind == DateTimeKind.Utc
            ? value
            : throw new ArgumentException("Timestamp must be a non-default UTC value.", parameterName);

    private static OrganizerPaymentProviderConnection CreateInternal(
        Guid id,
        Guid tenantId,
        Guid organizerActorId,
        string providerCode,
        string connectPlatformId,
        string externalAccountId,
        Guid? replacesConnectionId,
        DateTime createdAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || organizerActorId == Guid.Empty || replacesConnectionId == Guid.Empty)
        {
            throw new ArgumentException("Connection identities are required.");
        }

        return new OrganizerPaymentProviderConnection
        {
            Id = id,
            TenantId = tenantId,
            OrganizerActorId = organizerActorId,
            ProviderCode = NormalizeProviderCode(providerCode),
            ConnectPlatformId = NormalizeRequiredText(connectPlatformId, nameof(connectPlatformId), 120, preserveCase: false),
            ExternalAccountId = NormalizeRequiredText(externalAccountId, nameof(externalAccountId), 200, preserveCase: true),
            ActiveUniquenessSlot = ActiveUniquenessSlotValue,
            StatusId = (int)OrganizerPaymentProviderConnectionStatusEnum.PendingOnboarding,
            ReplacesConnectionId = replacesConnectionId,
            CreatedAt = EnsureUtc(createdAt, nameof(createdAt)),
            ConcurrencyStamp = Guid.CreateVersion7()
        }.AssignActiveScopeKey();
    }

    private OrganizerPaymentProviderConnection AssignActiveScopeKey()
    {
        ActiveScopeKey = string.Join('|', TenantId.ToString("N"), OrganizerActorId.ToString("N"), ProviderCode, ConnectPlatformId);
        return this;
    }

    private string CreateTerminalUniquenessSlot(string statusName) => $"{statusName.ToLowerInvariant()}:{Id:N}";

    private void MarkReplaced(DateTime replacedAt)
    {
        StatusId = (int)OrganizerPaymentProviderConnectionStatusEnum.Replaced;
        ActiveUniquenessSlot = CreateTerminalUniquenessSlot(nameof(OrganizerPaymentProviderConnectionStatusEnum.Replaced));
        ReplacedAt = replacedAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private void EnsureCanAcceptReadiness()
    {
        if (StatusId is (int)OrganizerPaymentProviderConnectionStatusEnum.Disabled or (int)OrganizerPaymentProviderConnectionStatusEnum.Replaced)
        {
            throw new InvalidOperationException("Terminal organizer payment connections cannot accept readiness evidence.");
        }
    }

    private void EnsureActiveForReplacement()
    {
        if (StatusId is (int)OrganizerPaymentProviderConnectionStatusEnum.Disabled or (int)OrganizerPaymentProviderConnectionStatusEnum.Replaced)
        {
            throw new InvalidOperationException("Disabled or replaced organizer payment connections cannot be replaced.");
        }
    }

    private void EnsureNotTerminal()
    {
        if (StatusId is (int)OrganizerPaymentProviderConnectionStatusEnum.Disabled or (int)OrganizerPaymentProviderConnectionStatusEnum.Replaced)
        {
            throw new InvalidOperationException("Organizer payment connection is already terminal.");
        }
    }

    private static string NormalizeReasonCode(string reasonCode)
    {
        string normalized = NormalizeRequiredText(reasonCode, nameof(reasonCode), 80, preserveCase: false).ToLowerInvariant();
        if (normalized.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_' && character != '-'))
        {
            throw new ArgumentException("Disable reason code must be bounded machine text.", nameof(reasonCode));
        }

        return normalized;
    }

    internal static string NormalizeProviderIdentity(string value, string parameterName, int maxLength, bool preserveCase) =>
        NormalizeRequiredText(value, parameterName, maxLength, preserveCase);

    internal static string NormalizeCountryCode(string merchantCountryCode)
    {
        string normalized = NormalizeRequiredText(merchantCountryCode, nameof(merchantCountryCode), 2, preserveCase: false).ToUpperInvariant();
        return normalized.Length == 2 && normalized.All(char.IsAsciiLetter)
            ? normalized
            : throw new ArgumentException("Merchant country must be ISO alpha-2.", nameof(merchantCountryCode));
    }

    private static string NormalizeRequiredText(string value, string parameterName, int maxLength, bool preserveCase)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is 0 || normalized.Length > maxLength || normalized.Any(char.IsControl))
        {
            throw new ArgumentException($"Value must be non-blank and at most {maxLength} characters.", parameterName);
        }

        return preserveCase ? normalized : normalized.ToLowerInvariant();
    }
}

public sealed record OrganizerPaymentRecipientSnapshotResult(
    bool Success,
    OrganizerPaymentRecipientSnapshot? Snapshot,
    string? FailureCode)
{
    public static OrganizerPaymentRecipientSnapshotResult Succeeded(OrganizerPaymentRecipientSnapshot snapshot) =>
        new(true, snapshot, null);

    public static OrganizerPaymentRecipientSnapshotResult Failure(string failureCode) =>
        new(false, null, failureCode);
}

public sealed class OrganizerPaymentProviderReadinessObservation
{
    private OrganizerPaymentProviderReadinessObservation()
    {
    }

    public string? MerchantCountryCode { get; private set; }
    public ChargeCapabilityState ChargeCapabilityState { get; private set; }
    public ProviderRequirementsState RequirementsState { get; private set; }
    public IReadOnlyList<string> SupportedCurrencyCodes { get; private set; } = [];
    public DateTime ObservedAt { get; private set; }
    public string EvidenceRevision { get; private set; } = string.Empty;
    public bool IsReady => MerchantCountryCode is not null
        && ChargeCapabilityState == ChargeCapabilityState.Active
        && RequirementsState == ProviderRequirementsState.Satisfied
        && SupportedCurrencyCodes.Count > 0;

    public static OrganizerPaymentProviderReadinessObservation Create(
        string? merchantCountryCode,
        ChargeCapabilityState chargeCapabilityState,
        ProviderRequirementsState requirementsState,
        IEnumerable<string> supportedCurrencyCodes,
        DateTime observedAt,
        string evidenceRevision)
    {
        if (!Enum.IsDefined(chargeCapabilityState) || !Enum.IsDefined(requirementsState))
        {
            throw new ArgumentException("Readiness state values must be known.");
        }

        string[] currencies = NormalizeCurrencyCodes(supportedCurrencyCodes);
        return new OrganizerPaymentProviderReadinessObservation
        {
            MerchantCountryCode = NormalizeCountry(merchantCountryCode),
            ChargeCapabilityState = chargeCapabilityState,
            RequirementsState = requirementsState,
            SupportedCurrencyCodes = currencies,
            ObservedAt = OrganizerPaymentProviderConnection.EnsureUtc(observedAt, nameof(observedAt)),
            EvidenceRevision = NormalizeEvidenceRevision(evidenceRevision)
        };
    }

    private static string? NormalizeCountry(string? merchantCountryCode)
    {
        if (string.IsNullOrWhiteSpace(merchantCountryCode))
        {
            return null;
        }

        string normalized = merchantCountryCode.Trim().ToUpperInvariant();
        return normalized.Length == 2 && normalized.All(char.IsAsciiLetter)
            ? normalized
            : throw new ArgumentException("Merchant country must be ISO alpha-2 when present.", nameof(merchantCountryCode));
    }

    private static string[] NormalizeCurrencyCodes(IEnumerable<string> supportedCurrencyCodes)
    {
        ArgumentNullException.ThrowIfNull(supportedCurrencyCodes);
        return supportedCurrencyCodes
            .Select(OrganizerPaymentProviderConnection.NormalizeCurrencyCode)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeEvidenceRevision(string evidenceRevision)
    {
        string normalized = evidenceRevision?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > 120 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Evidence revision must be bounded.", nameof(evidenceRevision));
        }

        return normalized;
    }
}

public enum ChargeCapabilityState
{
    Unknown = 0,
    Inactive = 1,
    Pending = 2,
    Active = 3
}

public enum ProviderRequirementsState
{
    Unknown = 0,
    CurrentlyDue = 1,
    EventuallyDue = 2,
    PastDue = 3,
    Satisfied = 4
}

public sealed class OrganizerPaymentProviderConnectionSupportedCurrency : ITenantEntity
{
    private OrganizerPaymentProviderConnectionSupportedCurrency()
    {
    }

    private OrganizerPaymentProviderConnectionSupportedCurrency(Guid tenantId, Guid connectionId, int ordinal, string currencyCode)
    {
        TenantId = tenantId;
        OrganizerPaymentProviderConnectionId = connectionId;
        Ordinal = ordinal;
        CurrencyCode = currencyCode;
    }

    public Guid TenantId { get; set; }

    public Guid OrganizerPaymentProviderConnectionId { get; private set; }

    public OrganizerPaymentProviderConnection? Connection { get; private set; }

    public int Ordinal { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    internal static OrganizerPaymentProviderConnectionSupportedCurrency Create(Guid tenantId, Guid connectionId, int ordinal, string currencyCode) =>
        new(tenantId, connectionId, ordinal, currencyCode);
}
