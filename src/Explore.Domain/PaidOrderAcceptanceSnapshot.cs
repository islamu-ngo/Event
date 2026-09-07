using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class PaidOrderAcceptanceSnapshot : ITenantEntity, IAuditableEntity
{
    public const int MaxDisplayNameLength = 200;
    public const int MaxDisclosureLength = 2000;
    public const int MaxContactLength = 320;
    public const string CurrentAcceptanceTemplateIdentifier = "paid-order-acceptance.v1";
    public const string CurrentAcceptanceTemplateText =
        "The buyer accepts the disclosed organizer merchant, tenant directory operator, instance operator, delivery, refund, support, payment-provider, and amount facts for this order.";
    private readonly List<PaidOrderAcceptanceLine> _lines = [];

    private PaidOrderAcceptanceSnapshot()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid RegistrationOrderId { get; private set; }
    public Guid EventId { get; private set; }
    public string CompositionRevision { get; private set; } = string.Empty;
    public string DisclosureRevision { get; private set; } = string.Empty;
    public string AcceptanceTemplateIdentifier { get; private set; } = string.Empty;
    public string AcceptanceTemplateText { get; private set; } = string.Empty;
    public Guid OrganizerActorId { get; private set; }
    public Guid OrganizerPaymentProviderConnectionId { get; private set; }
    public string ConnectPlatformId { get; private set; } = string.Empty;
    public string ExternalAccountId { get; private set; } = string.Empty;
    public string MerchantCountryCode { get; private set; } = string.Empty;
    public string MerchantDisclosureText { get; private set; } = string.Empty;

    public Guid TenantDirectoryOperatorDocumentId { get; private set; }
    public Guid TenantDirectoryOperatorRevisionId { get; private set; }
    public string TenantDirectoryOperatorPublicName { get; private set; } = string.Empty;
    public string TenantDirectoryOperatorLegalName { get; private set; } = string.Empty;
    public string TenantDirectoryOperatorKindCode { get; private set; } = string.Empty;
    public string TenantDirectoryOperatorCountryCode { get; private set; } = string.Empty;
    public string? TenantDirectoryOperatorRegistrationIdentifier { get; private set; }
    public string TenantDirectoryOperatorPublicContactEmail { get; private set; } = string.Empty;
    public string TenantDirectoryOperatorLegalNoticeUrl { get; private set; } = string.Empty;
    public string TenantDirectoryOperatorTermsUrl { get; private set; } = string.Empty;
    public string TenantDirectoryOperatorPrivacyUrl { get; private set; } = string.Empty;

    public Guid OperatorId { get; private set; }
    public string OperatorLegalName { get; private set; } = string.Empty;
    public string OperatorKindCode { get; private set; } = string.Empty;
    public string? OperatorRegistrationIdentifier { get; private set; }
    public string OperatorDisplayName { get; private set; } = string.Empty;
    public bool IsOfficialInstance { get; private set; }
    public string OfficialOrigin { get; private set; } = string.Empty;
    public string OperatorRegionCode { get; private set; } = string.Empty;
    public string OperatorWebsiteUrl { get; private set; } = string.Empty;
    public string OperatorLegalNoticeUrl { get; private set; } = string.Empty;
    public string OperatorTermsUrl { get; private set; } = string.Empty;
    public string OperatorPrivacyUrl { get; private set; } = string.Empty;
    public string ComplaintContact { get; private set; } = string.Empty;
    public string ComplaintOwner { get; private set; } = string.Empty;
    public string RefundOwner { get; private set; } = string.Empty;
    public string DisputeOwner { get; private set; } = string.Empty;
    public string ReconciliationOwner { get; private set; } = string.Empty;
    public string ActivationStatus { get; private set; } = string.Empty;

    public DateTimeOffset DeliveryStartsAtUtc { get; private set; }
    public DateTimeOffset DeliveryEndsAtUtc { get; private set; }
    public string EventTimeZoneId { get; private set; } = string.Empty;

    public string CurrencyCode { get; private set; } = string.Empty;
    public long OrganizerAmountMinor { get; private set; }
    public long PlatformFeeMinor { get; private set; }
    public long PlatformContributionMinor { get; private set; }
    public long TotalMinor { get; private set; }
    public Guid InstancePolicyVersionId { get; private set; }
    public Guid? TenantPolicyVersionId { get; private set; }
    public int RefundPolicyVersion { get; private set; }
    public string RefundPolicyText { get; private set; } = string.Empty;
    public string RefundPolicyLanguageTag { get; private set; } = string.Empty;
    public string SupportContact { get; private set; } = string.Empty;

    public string ProviderCode { get; private set; } = string.Empty;
    public string ProviderProfileCode { get; private set; } = string.Empty;
    public string ChargeType { get; private set; } = string.Empty;
    public string StatementDescriptor { get; private set; } = string.Empty;
    public string ProviderEnvironment { get; private set; } = string.Empty;
    public string ProviderCredentialOwner { get; private set; } = string.Empty;

    public DateTime AcceptedAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public IReadOnlyCollection<PaidOrderAcceptanceLine> Lines => _lines.OrderBy(line => line.Ordinal).ToArray();

    public PaidCheckoutTenantDirectoryOperatorDisclosure TenantDirectoryOperator =>
        PaidCheckoutTenantDirectoryOperatorDisclosure.Create(
            TenantDirectoryOperatorDocumentId,
            TenantDirectoryOperatorRevisionId,
            TenantDirectoryOperatorPublicName,
            TenantDirectoryOperatorLegalName,
            TenantDirectoryOperatorKindCode,
            TenantDirectoryOperatorCountryCode,
            TenantDirectoryOperatorRegistrationIdentifier,
            TenantDirectoryOperatorPublicContactEmail,
            TenantDirectoryOperatorLegalNoticeUrl,
            TenantDirectoryOperatorTermsUrl,
            TenantDirectoryOperatorPrivacyUrl);

    public PaidCheckoutOperatorDisclosure Operator => PaidCheckoutOperatorDisclosure.Create(
        OperatorId, OperatorDisplayName, IsOfficialInstance, OfficialOrigin, OperatorRegionCode, OperatorWebsiteUrl,
        OperatorLegalNoticeUrl, OperatorTermsUrl, OperatorPrivacyUrl, ComplaintContact, ComplaintOwner, RefundOwner,
        DisputeOwner, ReconciliationOwner, ActivationStatus);

    public PaidCheckoutInstanceOperatorDisclosure InstanceOperator => new(
        OperatorId, OperatorDisplayName, OperatorLegalName, OperatorKindCode,
        OperatorRegistrationIdentifier, IsOfficialInstance, OfficialOrigin, OperatorRegionCode,
        OperatorWebsiteUrl, OperatorLegalNoticeUrl, OperatorTermsUrl, OperatorPrivacyUrl);

    public PaidCheckoutPaymentOperationsDisclosure PaymentOperations => new(
        ComplaintContact, ComplaintOwner, RefundOwner, DisputeOwner, ReconciliationOwner,
        ActivationStatus);

    public PaidOrderDeliverySnapshot Delivery => PaidOrderDeliverySnapshot.Create(
        DeliveryStartsAtUtc, DeliveryEndsAtUtc, EventTimeZoneId);

    public PaidCheckoutProviderDisclosure Provider => PaidCheckoutProviderDisclosure.Create(
        ProviderCode, ProviderProfileCode, ChargeType, StatementDescriptor, ProviderEnvironment, ProviderCredentialOwner);

    public static PaidOrderAcceptanceSnapshot Create(
        Guid id,
        Guid tenantId,
        Guid orderTenantId,
        Guid registrationOrderId,
        Guid eventId,
        string compositionRevision,
        string disclosureRevision,
        string acceptanceTemplateIdentifier,
        string acceptanceTemplateText,
        Guid organizerActorId,
        string merchantDisclosureText,
        PaidCheckoutTenantDirectoryOperatorDisclosure tenantDirectoryOperator,
        PaidCheckoutOperatorDisclosure operatorDisclosure,
        PaidOrderDeliverySnapshot delivery,
        string currencyCode,
        long organizerAmountMinor,
        long platformFeeMinor,
        long platformContributionMinor,
        long totalMinor,
        Guid instancePolicyVersionId,
        int refundPolicyVersion,
        string refundPolicyText,
        string refundPolicyLanguageTag,
        string supportContact,
        PaidCheckoutProviderDisclosure provider,
        IReadOnlyCollection<PaidOrderAcceptanceLineFact> lines,
        DateTime acceptedAt,
        Guid? tenantPolicyVersionId = null,
        Guid organizerPaymentProviderConnectionId = default,
        string connectPlatformId = "",
        string externalAccountId = "",
        string merchantCountryCode = "")
    {
        ArgumentNullException.ThrowIfNull(tenantDirectoryOperator);
        ArgumentNullException.ThrowIfNull(operatorDisclosure);
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(lines);
        if (id == Guid.Empty || tenantId == Guid.Empty || orderTenantId != tenantId || registrationOrderId == Guid.Empty ||
            eventId == Guid.Empty || organizerActorId == Guid.Empty || organizerPaymentProviderConnectionId == Guid.Empty ||
            instancePolicyVersionId == Guid.Empty ||
            refundPolicyVersion <= 0)
        {
            throw new ArgumentException("Acceptance identity, policy, and tenant lineage are required.");
        }

        string currency = CurrencyMetadata.Get(currencyCode).Code;
        if (CurrencyMetadata.Get(currency).IsNoCurrency || organizerAmountMinor <= 0 || platformFeeMinor < 0 ||
            platformFeeMinor > organizerAmountMinor || platformContributionMinor < 0 ||
            totalMinor != MinorUnitMath.Add(organizerAmountMinor, platformContributionMinor))
        {
            throw new ArgumentException("Acceptance money facts are invalid.");
        }

        PaidOrderAcceptanceLineFact[] normalizedLines = lines.ToArray();
        if (normalizedLines.Length == 0 || normalizedLines.Select(line => line.OrderLineId).Distinct().Count() != normalizedLines.Length ||
            checked(normalizedLines.Sum(line => line.LineTotalMinor)) != organizerAmountMinor)
        {
            throw new ArgumentException("Acceptance lines must be unique, non-empty, and total to the organizer amount.", nameof(lines));
        }

        DateTime timestamp = OrganizerPaymentProviderConnection.EnsureUtc(acceptedAt, nameof(acceptedAt));
        var snapshot = new PaidOrderAcceptanceSnapshot
        {
            Id = id,
            TenantId = tenantId,
            RegistrationOrderId = registrationOrderId,
            EventId = eventId,
            CompositionRevision = PaidCheckoutDisclosureValidation.Required(compositionRevision, nameof(compositionRevision), 80),
            DisclosureRevision = PaidCheckoutDisclosureValidation.Required(disclosureRevision, nameof(disclosureRevision), 80),
            AcceptanceTemplateIdentifier = PaidCheckoutDisclosureValidation.Required(
                acceptanceTemplateIdentifier, nameof(acceptanceTemplateIdentifier), 80),
            AcceptanceTemplateText = PaidCheckoutDisclosureValidation.Required(
                acceptanceTemplateText, nameof(acceptanceTemplateText), MaxDisclosureLength),
            OrganizerActorId = organizerActorId,
            OrganizerPaymentProviderConnectionId = organizerPaymentProviderConnectionId,
            ConnectPlatformId = OrganizerPaymentProviderConnection.NormalizeProviderIdentity(connectPlatformId, nameof(connectPlatformId), 120, preserveCase: false),
            ExternalAccountId = OrganizerPaymentProviderConnection.NormalizeProviderIdentity(externalAccountId, nameof(externalAccountId), 200, preserveCase: true),
            MerchantCountryCode = OrganizerPaymentProviderConnection.NormalizeCountryCode(merchantCountryCode),
            MerchantDisclosureText = PaidCheckoutDisclosureValidation.Required(merchantDisclosureText, nameof(merchantDisclosureText), MaxDisclosureLength),
            TenantDirectoryOperatorDocumentId = tenantDirectoryOperator.DocumentId,
            TenantDirectoryOperatorRevisionId = tenantDirectoryOperator.DocumentRevisionId,
            TenantDirectoryOperatorPublicName = tenantDirectoryOperator.PublicName,
            TenantDirectoryOperatorLegalName = tenantDirectoryOperator.LegalName,
            TenantDirectoryOperatorKindCode = tenantDirectoryOperator.OperatorKindCode,
            TenantDirectoryOperatorCountryCode = tenantDirectoryOperator.JurisdictionCountryCode,
            TenantDirectoryOperatorRegistrationIdentifier = tenantDirectoryOperator.RegistrationIdentifier,
            TenantDirectoryOperatorPublicContactEmail = tenantDirectoryOperator.PublicContactEmail,
            TenantDirectoryOperatorLegalNoticeUrl = tenantDirectoryOperator.LegalNoticeUrl,
            TenantDirectoryOperatorTermsUrl = tenantDirectoryOperator.TermsUrl,
            TenantDirectoryOperatorPrivacyUrl = tenantDirectoryOperator.PrivacyUrl,
            OperatorId = operatorDisclosure.OperatorId,
            OperatorLegalName = operatorDisclosure.LegalName,
            OperatorKindCode = operatorDisclosure.OperatorKindCode,
            OperatorRegistrationIdentifier = operatorDisclosure.RegistrationIdentifier,
            OperatorDisplayName = operatorDisclosure.OperatorDisplayName,
            IsOfficialInstance = operatorDisclosure.IsOfficialInstance,
            OfficialOrigin = operatorDisclosure.OfficialOrigin,
            OperatorRegionCode = operatorDisclosure.RegionCode,
            OperatorWebsiteUrl = operatorDisclosure.WebsiteUrl,
            OperatorLegalNoticeUrl = operatorDisclosure.LegalNoticeUrl,
            OperatorTermsUrl = operatorDisclosure.TermsUrl,
            OperatorPrivacyUrl = operatorDisclosure.PrivacyUrl,
            ComplaintContact = operatorDisclosure.ComplaintContact,
            ComplaintOwner = operatorDisclosure.ComplaintOwner,
            RefundOwner = operatorDisclosure.RefundOwner,
            DisputeOwner = operatorDisclosure.DisputeOwner,
            ReconciliationOwner = operatorDisclosure.ReconciliationOwner,
            ActivationStatus = operatorDisclosure.ActivationStatus,
            DeliveryStartsAtUtc = delivery.StartsAtUtc,
            DeliveryEndsAtUtc = delivery.EndsAtUtc,
            EventTimeZoneId = delivery.TimeZoneId,
            CurrencyCode = currency,
            OrganizerAmountMinor = organizerAmountMinor,
            PlatformFeeMinor = platformFeeMinor,
            PlatformContributionMinor = platformContributionMinor,
            TotalMinor = totalMinor,
            InstancePolicyVersionId = instancePolicyVersionId,
            TenantPolicyVersionId = tenantPolicyVersionId,
            RefundPolicyVersion = refundPolicyVersion,
            RefundPolicyText = PaidCheckoutDisclosureValidation.Required(refundPolicyText, nameof(refundPolicyText), MaxDisclosureLength),
            RefundPolicyLanguageTag = PaidCheckoutDisclosureValidation.Required(refundPolicyLanguageTag, nameof(refundPolicyLanguageTag), 35),
            SupportContact = PaidCheckoutDisclosureValidation.Required(supportContact, nameof(supportContact), MaxContactLength),
            ProviderCode = provider.ProviderCode,
            ProviderProfileCode = provider.ProfileCode,
            ChargeType = provider.ChargeType,
            StatementDescriptor = provider.StatementDescriptor,
            ProviderEnvironment = provider.Environment,
            ProviderCredentialOwner = provider.CredentialOwner,
            AcceptedAt = timestamp,
            CreatedAt = timestamp
        };
        snapshot._lines.AddRange(normalizedLines.Select((line, ordinal) => PaidOrderAcceptanceLine.Create(
            tenantId, id, line, ordinal)));
        return snapshot;
    }

    public bool IsCurrent(
        string compositionRevision,
        string disclosureRevision,
        Guid instancePolicyVersionId,
        Guid? tenantPolicyVersionId,
        string providerCode,
        string providerProfileCode,
        string? providerEnvironment = null,
        string? providerCredentialOwner = null) =>
        string.Equals(CompositionRevision, compositionRevision, StringComparison.Ordinal) &&
        string.Equals(DisclosureRevision, disclosureRevision, StringComparison.Ordinal) &&
        InstancePolicyVersionId == instancePolicyVersionId &&
        TenantPolicyVersionId == tenantPolicyVersionId &&
        string.Equals(ProviderCode, providerCode, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(ProviderProfileCode, providerProfileCode, StringComparison.Ordinal) &&
        (providerEnvironment is null || string.Equals(ProviderEnvironment, providerEnvironment, StringComparison.Ordinal)) &&
        (providerCredentialOwner is null || string.Equals(ProviderCredentialOwner, providerCredentialOwner, StringComparison.Ordinal));

    public bool MatchesLineFacts(IEnumerable<PaidOrderAcceptanceLineFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        PaidOrderAcceptanceLineFact[] expected = facts.OrderBy(line => line.OrderLineId).ToArray();
        PaidOrderAcceptanceLine[] actual = _lines.OrderBy(line => line.OrderLineId).ToArray();
        return expected.Length == actual.Length && expected.Zip(actual).All(pair =>
            pair.First.OrderLineId == pair.Second.OrderLineId &&
            string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal) &&
            pair.First.Quantity == pair.Second.Quantity &&
            pair.First.UnitAmountMinor == pair.Second.UnitAmountMinor &&
            pair.First.DiscountAmountMinor == pair.Second.DiscountAmountMinor &&
            pair.First.LineTotalMinor == pair.Second.LineTotalMinor);
    }
}

public sealed class PaidOrderAcceptanceLine : ITenantEntity
{
    private PaidOrderAcceptanceLine()
    {
    }

    public Guid TenantId { get; set; }
    public Guid PaidOrderAcceptanceSnapshotId { get; private set; }
    public Guid OrderLineId { get; private set; }
    public int Ordinal { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public long UnitAmountMinor { get; private set; }
    public long DiscountAmountMinor { get; private set; }
    public long LineTotalMinor { get; private set; }

    internal static PaidOrderAcceptanceLine Create(
        Guid tenantId,
        Guid acceptanceSnapshotId,
        PaidOrderAcceptanceLineFact fact,
        int ordinal)
    {
        if (tenantId == Guid.Empty || acceptanceSnapshotId == Guid.Empty || ordinal < 0)
        {
            throw new ArgumentException("Acceptance line lineage and ordinal are required.");
        }
        return new()
        {
            TenantId = tenantId,
            PaidOrderAcceptanceSnapshotId = acceptanceSnapshotId,
            OrderLineId = fact.OrderLineId,
            Ordinal = ordinal,
            Name = fact.Name,
            Quantity = fact.Quantity,
            UnitAmountMinor = fact.UnitAmountMinor,
            DiscountAmountMinor = fact.DiscountAmountMinor,
            LineTotalMinor = fact.LineTotalMinor
        };
    }
}
