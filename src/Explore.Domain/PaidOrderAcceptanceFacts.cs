using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed record PaidCheckoutInstanceOperatorDisclosure(
    Guid OperatorId,
    string PublicName,
    string LegalName,
    string OperatorKindCode,
    string? RegistrationIdentifier,
    bool IsOfficialInstance,
    string OfficialOrigin,
    string JurisdictionCountryCode,
    string WebsiteUrl,
    string LegalNoticeUrl,
    string TermsUrl,
    string PrivacyUrl);

public sealed record PaidCheckoutPaymentOperationsDisclosure(
    string ComplaintContact,
    string ComplaintOwner,
    string RefundOwner,
    string DisputeOwner,
    string ReconciliationOwner,
    string ActivationStatus);

public sealed record PaidOrderDeliverySnapshot
{
    private PaidOrderDeliverySnapshot(DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc, string timeZoneId)
    {
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        TimeZoneId = timeZoneId;
    }

    public DateTimeOffset StartsAtUtc { get; }
    public DateTimeOffset EndsAtUtc { get; }
    public string TimeZoneId { get; }

    public static PaidOrderDeliverySnapshot Create(DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc, string timeZoneId)
    {
        DateTimeOffset start = startsAtUtc.ToUniversalTime();
        DateTimeOffset end = endsAtUtc.ToUniversalTime();
        if (end <= start)
        {
            throw new ArgumentException("Delivery end must be after delivery start.", nameof(endsAtUtc));
        }

        string zone = Required(timeZoneId, nameof(timeZoneId), 100);
        return new(start, end, zone);
    }

    private static string Required(string? value, string parameterName, int maxLength) =>
        PaidCheckoutDisclosureValidation.Required(value, parameterName, maxLength);
}

public sealed record PaidCheckoutTenantDirectoryOperatorDisclosure
{
    private PaidCheckoutTenantDirectoryOperatorDisclosure(
        Guid documentId,
        Guid documentRevisionId,
        string publicName,
        string legalName,
        string operatorKindCode,
        string jurisdictionCountryCode,
        string? registrationIdentifier,
        string publicContactEmail,
        string legalNoticeUrl,
        string termsUrl,
        string privacyUrl)
    {
        DocumentId = documentId;
        DocumentRevisionId = documentRevisionId;
        PublicName = publicName;
        LegalName = legalName;
        OperatorKindCode = operatorKindCode;
        JurisdictionCountryCode = jurisdictionCountryCode;
        RegistrationIdentifier = registrationIdentifier;
        PublicContactEmail = publicContactEmail;
        LegalNoticeUrl = legalNoticeUrl;
        TermsUrl = termsUrl;
        PrivacyUrl = privacyUrl;
    }

    public Guid DocumentId { get; }
    public Guid DocumentRevisionId { get; }
    public string PublicName { get; }
    public string LegalName { get; }
    public string OperatorKindCode { get; }
    public string JurisdictionCountryCode { get; }
    public string? RegistrationIdentifier { get; }
    public string PublicContactEmail { get; }
    public string LegalNoticeUrl { get; }
    public string TermsUrl { get; }
    public string PrivacyUrl { get; }

    public static PaidCheckoutTenantDirectoryOperatorDisclosure Create(
        Guid documentId,
        Guid documentRevisionId,
        string publicName,
        string legalName,
        string operatorKindCode,
        string jurisdictionCountryCode,
        string? registrationIdentifier,
        string publicContactEmail,
        string legalNoticeUrl,
        string termsUrl,
        string privacyUrl)
    {
        if (documentId == Guid.Empty || documentRevisionId == Guid.Empty)
        {
            throw new ArgumentException("Directory operator document lineage is invalid.");
        }

        string country = PaidCheckoutDisclosureValidation.Required(
            jurisdictionCountryCode, nameof(jurisdictionCountryCode), 2).ToUpperInvariant();
        if (country.Length != 2 || country.Any(character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException("Directory operator country must be an ISO alpha-2 code.", nameof(jurisdictionCountryCode));
        }

        return new(
            documentId,
            documentRevisionId,
            PaidCheckoutDisclosureValidation.Required(publicName, nameof(publicName), 200),
            PaidCheckoutDisclosureValidation.Required(legalName, nameof(legalName), 300),
            PaidCheckoutDisclosureValidation.Required(operatorKindCode, nameof(operatorKindCode), 80).ToLowerInvariant(),
            country,
            string.IsNullOrWhiteSpace(registrationIdentifier)
                ? null
                : PaidCheckoutDisclosureValidation.Required(registrationIdentifier, nameof(registrationIdentifier), 120),
            PaidCheckoutDisclosureValidation.Required(publicContactEmail, nameof(publicContactEmail), 320).ToLowerInvariant(),
            PaidCheckoutDisclosureValidation.HttpsUrl(legalNoticeUrl, nameof(legalNoticeUrl)),
            PaidCheckoutDisclosureValidation.HttpsUrl(termsUrl, nameof(termsUrl)),
            PaidCheckoutDisclosureValidation.HttpsUrl(privacyUrl, nameof(privacyUrl)));
    }
}

public sealed record PaidCheckoutOperatorDisclosure
{
    private PaidCheckoutOperatorDisclosure(
        Guid operatorId,
        string operatorDisplayName,
        bool isOfficialInstance,
        string officialOrigin,
        string regionCode,
        string websiteUrl,
        string legalNoticeUrl,
        string termsUrl,
        string privacyUrl,
        string complaintContact,
        string complaintOwner,
        string refundOwner,
        string disputeOwner,
        string reconciliationOwner,
        string activationStatus,
        string legalName,
        string operatorKindCode,
        string? registrationIdentifier)
    {
        OperatorId = operatorId;
        OperatorDisplayName = operatorDisplayName;
        IsOfficialInstance = isOfficialInstance;
        OfficialOrigin = officialOrigin;
        RegionCode = regionCode;
        WebsiteUrl = websiteUrl;
        LegalNoticeUrl = legalNoticeUrl;
        TermsUrl = termsUrl;
        PrivacyUrl = privacyUrl;
        ComplaintContact = complaintContact;
        ComplaintOwner = complaintOwner;
        RefundOwner = refundOwner;
        DisputeOwner = disputeOwner;
        ReconciliationOwner = reconciliationOwner;
        ActivationStatus = activationStatus;
        LegalName = legalName;
        OperatorKindCode = operatorKindCode;
        RegistrationIdentifier = registrationIdentifier;
    }

    public Guid OperatorId { get; }
    public string OperatorDisplayName { get; }
    public bool IsOfficialInstance { get; }
    public string OfficialOrigin { get; }
    public string RegionCode { get; }
    public string WebsiteUrl { get; }
    public string LegalNoticeUrl { get; }
    public string TermsUrl { get; }
    public string PrivacyUrl { get; }
    public string ComplaintContact { get; }
    public string ComplaintOwner { get; }
    public string RefundOwner { get; }
    public string DisputeOwner { get; }
    public string ReconciliationOwner { get; }
    public string ActivationStatus { get; }
    public string LegalName { get; }
    public string OperatorKindCode { get; }
    public string? RegistrationIdentifier { get; }

    public static PaidCheckoutOperatorDisclosure Create(
        Guid operatorId,
        string operatorDisplayName,
        bool isOfficialInstance,
        string officialOrigin,
        string regionCode,
        string websiteUrl,
        string legalNoticeUrl,
        string termsUrl,
        string privacyUrl,
        string complaintContact,
        string complaintOwner,
        string refundOwner,
        string disputeOwner,
        string reconciliationOwner,
        string activationStatus,
        string? legalName = null,
        string? operatorKindCode = null,
        string? registrationIdentifier = null)
    {
        if (operatorId == Guid.Empty)
        {
            throw new ArgumentException("Operator identity is required.", nameof(operatorId));
        }

        string origin = PaidCheckoutDisclosureValidation.HttpsUrl(officialOrigin, nameof(officialOrigin), originOnly: true);
        string region = PaidCheckoutDisclosureValidation.Required(regionCode, nameof(regionCode), 8).ToUpperInvariant();
        if (region.Length is < 2 or > 3 || region.Any(character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException("Operator region must be an ISO-style alphabetic region code.", nameof(regionCode));
        }

        string activation = PaidCheckoutDisclosureValidation.Required(activationStatus, nameof(activationStatus), 32).ToLowerInvariant();
        if (activation is not ("approved" or "suspended"))
        {
            throw new ArgumentException("Operator activation status must be startup-approved or suspended.", nameof(activationStatus));
        }

        return new(
            operatorId,
            PaidCheckoutDisclosureValidation.Required(operatorDisplayName, nameof(operatorDisplayName), 200),
            isOfficialInstance,
            origin,
            region,
            PaidCheckoutDisclosureValidation.HttpsUrl(websiteUrl, nameof(websiteUrl)),
            PaidCheckoutDisclosureValidation.HttpsUrl(legalNoticeUrl, nameof(legalNoticeUrl)),
            PaidCheckoutDisclosureValidation.HttpsUrl(termsUrl, nameof(termsUrl)),
            PaidCheckoutDisclosureValidation.HttpsUrl(privacyUrl, nameof(privacyUrl)),
            PaidCheckoutDisclosureValidation.Required(complaintContact, nameof(complaintContact), 320),
            PaidCheckoutDisclosureValidation.Required(complaintOwner, nameof(complaintOwner), 200),
            PaidCheckoutDisclosureValidation.Required(refundOwner, nameof(refundOwner), 200),
            PaidCheckoutDisclosureValidation.Required(disputeOwner, nameof(disputeOwner), 200),
            PaidCheckoutDisclosureValidation.Required(reconciliationOwner, nameof(reconciliationOwner), 200),
            activation,
            PaidCheckoutDisclosureValidation.Required(legalName ?? operatorDisplayName, nameof(legalName), 300),
            PaidCheckoutDisclosureValidation.Required(operatorKindCode ?? "independent_operator", nameof(operatorKindCode), 80).ToLowerInvariant(),
            string.IsNullOrWhiteSpace(registrationIdentifier)
                ? null
                : PaidCheckoutDisclosureValidation.Required(registrationIdentifier, nameof(registrationIdentifier), 120));
    }
}

public sealed record PaidCheckoutProviderDisclosure
{
    private PaidCheckoutProviderDisclosure(
        string providerCode,
        string profileCode,
        string chargeType,
        string statementDescriptor,
        string environment,
        string credentialOwner)
    {
        ProviderCode = providerCode;
        ProfileCode = profileCode;
        ChargeType = chargeType;
        StatementDescriptor = statementDescriptor;
        Environment = environment;
        CredentialOwner = credentialOwner;
    }

    public string ProviderCode { get; }
    public string ProfileCode { get; }
    public string ChargeType { get; }
    public string StatementDescriptor { get; }
    public string Environment { get; }
    public string CredentialOwner { get; }

    public static PaidCheckoutProviderDisclosure Create(
        string providerCode,
        string profileCode,
        string chargeType,
        string statementDescriptor,
        string environment,
        string credentialOwner)
    {
        string normalizedEnvironment = PaidCheckoutDisclosureValidation.Required(environment, nameof(environment), 16).ToLowerInvariant();
        if (normalizedEnvironment is not ("test" or "live"))
        {
            throw new ArgumentException("Payment environment must be test or live.", nameof(environment));
        }

        return new(
            PaidCheckoutDisclosureValidation.Required(providerCode, nameof(providerCode), 40).ToLowerInvariant(),
            PaidCheckoutDisclosureValidation.Required(profileCode, nameof(profileCode), 40),
            PaidCheckoutDisclosureValidation.Required(chargeType, nameof(chargeType), 40).ToLowerInvariant(),
            PaidCheckoutDisclosureValidation.Required(statementDescriptor, nameof(statementDescriptor), 22),
            normalizedEnvironment,
            PaidCheckoutDisclosureValidation.Required(credentialOwner, nameof(credentialOwner), 80).ToLowerInvariant());
    }
}

public sealed record PaidOrderAcceptanceLineFact
{
    private PaidOrderAcceptanceLineFact(
        Guid orderLineId,
        string name,
        int quantity,
        long unitAmountMinor,
        long discountAmountMinor,
        long lineTotalMinor)
    {
        OrderLineId = orderLineId;
        Name = name;
        Quantity = quantity;
        UnitAmountMinor = unitAmountMinor;
        DiscountAmountMinor = discountAmountMinor;
        LineTotalMinor = lineTotalMinor;
    }

    public Guid OrderLineId { get; }
    public string Name { get; }
    public int Quantity { get; }
    public long UnitAmountMinor { get; }
    public long DiscountAmountMinor { get; }
    public long LineTotalMinor { get; }

    public static PaidOrderAcceptanceLineFact Create(
        Guid orderLineId,
        string name,
        int quantity,
        long unitAmountMinor,
        long discountAmountMinor,
        long lineTotalMinor)
    {
        if (orderLineId == Guid.Empty)
        {
            throw new ArgumentException("Order line identity is required.", nameof(orderLineId));
        }
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }
        if (unitAmountMinor < 0 || discountAmountMinor < 0 || lineTotalMinor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitAmountMinor), "Line money cannot be negative.");
        }

        long gross = checked(unitAmountMinor * quantity);
        if (discountAmountMinor > gross || checked(gross - discountAmountMinor) != lineTotalMinor)
        {
            throw new ArgumentException("Acceptance line money must equal quantity times unit amount less discount.");
        }

        return new(orderLineId, PaidCheckoutDisclosureValidation.Required(name, nameof(name), 300), quantity,
            unitAmountMinor, discountAmountMinor, lineTotalMinor);
    }

    public static PaidOrderAcceptanceLineFact FromSnapshot(PaidOrderAcceptanceLine line) =>
        Create(line.OrderLineId, line.Name, line.Quantity, line.UnitAmountMinor, line.DiscountAmountMinor, line.LineTotalMinor);
}

internal static class PaidCheckoutDisclosureValidation
{
    internal static string Required(string? value, string parameterName, int maxLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is 0 || normalized.Length > maxLength || normalized.Any(char.IsControl))
        {
            throw new ArgumentException($"Value must be non-blank and at most {maxLength} characters.", parameterName);
        }
        return normalized;
    }

    internal static string HttpsUrl(string? value, string parameterName, bool originOnly = false)
    {
        string normalized = Required(value, parameterName, 500);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length != 0 || (originOnly && (uri.PathAndQuery != "/" || uri.Fragment.Length != 0)))
        {
            throw new ArgumentException("Disclosure URL must be an absolute HTTPS URL without credentials.", parameterName);
        }
        return originOnly ? uri.GetLeftPart(UriPartial.Authority) : uri.AbsoluteUri;
    }
}
