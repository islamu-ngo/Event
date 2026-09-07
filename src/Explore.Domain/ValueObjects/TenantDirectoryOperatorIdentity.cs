namespace Explore.Domain.ValueObjects;

using System.Collections.Immutable;
using System.Net.Mail;
using System.Text;
using Explore.Domain.Settings.Documents.Payloads;

public enum TenantDirectoryOperatorIdentityCapability
{
    Activation,
    PublicDisclosure,
    PaidCommerce
}

public static class TenantDirectoryOperatorKinds
{
    public const string RegisteredOrganization = "registered_organization";
    public const string SoleTrader = "sole_trader";
    public const string Individual = "individual";
    public const string PublicBody = "public_body";
    public const string UnincorporatedAssociation = "unincorporated_association";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        RegisteredOrganization,
        SoleTrader,
        Individual,
        PublicBody,
        UnincorporatedAssociation
    };
}

public static class TenantDirectoryOperatorIdentityReasonCodes
{
    public const string MissingPublicName = "tenant_directory_operator_identity_public_name_missing";
    public const string InvalidPublicName = "tenant_directory_operator_identity_public_name_invalid";
    public const string MissingLegalName = "tenant_directory_operator_identity_legal_name_missing";
    public const string InvalidLegalName = "tenant_directory_operator_identity_legal_name_invalid";
    public const string MissingOperatorKind = "tenant_directory_operator_identity_operator_kind_missing";
    public const string InvalidOperatorKind = "tenant_directory_operator_identity_operator_kind_invalid";
    public const string MissingJurisdictionCountry = "tenant_directory_operator_identity_jurisdiction_country_missing";
    public const string InvalidJurisdictionCountry = "tenant_directory_operator_identity_jurisdiction_country_invalid";
    public const string MissingPublicContactEmail = "tenant_directory_operator_identity_public_contact_email_missing";
    public const string InvalidPublicContactEmail = "tenant_directory_operator_identity_public_contact_email_invalid";
    public const string MissingLegalNoticeUrl = "tenant_directory_operator_identity_legal_notice_url_missing";
    public const string InvalidLegalNoticeUrl = "tenant_directory_operator_identity_legal_notice_url_invalid";
    public const string MissingTermsUrl = "tenant_directory_operator_identity_terms_url_missing";
    public const string InvalidTermsUrl = "tenant_directory_operator_identity_terms_url_invalid";
    public const string MissingPrivacyUrl = "tenant_directory_operator_identity_privacy_url_missing";
    public const string InvalidPrivacyUrl = "tenant_directory_operator_identity_privacy_url_invalid";
    public const string InvalidRegistrationIdentifier = "tenant_directory_operator_identity_registration_identifier_invalid";
}

public sealed record TenantDirectoryOperatorIdentityReadiness(
    bool IsReady,
    TenantDirectoryOperatorIdentity? Identity,
    ImmutableArray<string> ReasonCodes);

public sealed record TenantDirectoryOperatorIdentityDraftValidation(
    TenantDirectoryOperatorIdentitySettings NormalizedSettings,
    ImmutableArray<string> ReasonCodes)
{
    public bool IsValid => ReasonCodes.IsEmpty;
}

public sealed record TenantDirectoryOperatorIdentity
{
    public const int MaxPublicNameLength = 200;
    public const int MaxLegalNameLength = 300;
    public const int MaxRegistrationIdentifierLength = 120;
    public const int MaxPublicContactEmailLength = 320;
    public const int MaxLegalUrlLength = 2048;

    private TenantDirectoryOperatorIdentity(
        string publicName,
        string legalName,
        string operatorKindCode,
        string jurisdictionCountryCode,
        string? registrationIdentifier,
        string publicContactEmail,
        string legalNoticeUrl,
        string? termsUrl,
        string privacyUrl)
    {
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

    public string PublicName { get; }
    public string LegalName { get; }
    public string OperatorKindCode { get; }
    public string JurisdictionCountryCode { get; }
    public string? RegistrationIdentifier { get; }
    public string PublicContactEmail { get; }
    public string LegalNoticeUrl { get; }
    public string? TermsUrl { get; }
    public string PrivacyUrl { get; }

    public TenantDirectoryOperatorIdentitySettings ToSettings() => new()
    {
        PublicName = PublicName,
        LegalName = LegalName,
        OperatorKindCode = OperatorKindCode,
        JurisdictionCountryCode = JurisdictionCountryCode,
        RegistrationIdentifier = RegistrationIdentifier,
        PublicContactEmail = PublicContactEmail,
        LegalNoticeUrl = LegalNoticeUrl,
        TermsUrl = TermsUrl,
        PrivacyUrl = PrivacyUrl
    };

    public static TenantDirectoryOperatorIdentityDraftValidation ValidateDraft(
        TenantDirectoryOperatorIdentitySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var reasons = ImmutableArray.CreateBuilder<string>();
        string? publicName = NormalizeOptionalText(settings.PublicName);
        AddInvalidBoundedTextReason(
            publicName,
            MaxPublicNameLength,
            TenantDirectoryOperatorIdentityReasonCodes.InvalidPublicName,
            reasons);
        string? legalName = NormalizeOptionalText(settings.LegalName);
        AddInvalidBoundedTextReason(
            legalName,
            MaxLegalNameLength,
            TenantDirectoryOperatorIdentityReasonCodes.InvalidLegalName,
            reasons);

        string? operatorKind = NormalizeOptionalText(settings.OperatorKindCode)?.ToLowerInvariant();
        if (operatorKind is not null && !TenantDirectoryOperatorKinds.All.Contains(operatorKind))
        {
            reasons.Add(TenantDirectoryOperatorIdentityReasonCodes.InvalidOperatorKind);
        }

        string? countryCode = NormalizeOptionalText(settings.JurisdictionCountryCode)?.ToUpperInvariant();
        if (countryCode is not null && !IsIsoAlpha2Shape(countryCode))
        {
            reasons.Add(TenantDirectoryOperatorIdentityReasonCodes.InvalidJurisdictionCountry);
        }

        string? registrationIdentifier = NormalizeOptionalText(settings.RegistrationIdentifier);
        AddInvalidBoundedTextReason(
            registrationIdentifier,
            MaxRegistrationIdentifierLength,
            TenantDirectoryOperatorIdentityReasonCodes.InvalidRegistrationIdentifier,
            reasons);

        string? publicContactEmail = NormalizeEmail(settings.PublicContactEmail);
        if (!string.IsNullOrWhiteSpace(settings.PublicContactEmail) && publicContactEmail is null)
        {
            reasons.Add(TenantDirectoryOperatorIdentityReasonCodes.InvalidPublicContactEmail);
        }

        string? legalNoticeUrl = NormalizeHttpsUrl(settings.LegalNoticeUrl);
        AddInvalidOptionalUrlReason(
            settings.LegalNoticeUrl,
            legalNoticeUrl,
            TenantDirectoryOperatorIdentityReasonCodes.InvalidLegalNoticeUrl,
            reasons);
        string? termsUrl = NormalizeHttpsUrl(settings.TermsUrl);
        AddInvalidOptionalUrlReason(
            settings.TermsUrl,
            termsUrl,
            TenantDirectoryOperatorIdentityReasonCodes.InvalidTermsUrl,
            reasons);
        string? privacyUrl = NormalizeHttpsUrl(settings.PrivacyUrl);
        AddInvalidOptionalUrlReason(
            settings.PrivacyUrl,
            privacyUrl,
            TenantDirectoryOperatorIdentityReasonCodes.InvalidPrivacyUrl,
            reasons);

        return new(
            new TenantDirectoryOperatorIdentitySettings
            {
                PublicName = publicName,
                LegalName = legalName,
                OperatorKindCode = operatorKind,
                JurisdictionCountryCode = countryCode,
                RegistrationIdentifier = registrationIdentifier,
                PublicContactEmail = publicContactEmail,
                LegalNoticeUrl = legalNoticeUrl,
                TermsUrl = termsUrl,
                PrivacyUrl = privacyUrl
            },
            reasons.ToImmutable());
    }

    public static TenantDirectoryOperatorIdentityReadiness Evaluate(
        TenantDirectoryOperatorIdentitySettings settings,
        TenantDirectoryOperatorIdentityCapability capability)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(capability))
        {
            throw new ArgumentOutOfRangeException(nameof(capability), capability, "Unsupported identity capability.");
        }

        var reasons = ImmutableArray.CreateBuilder<string>();
        string? publicName = NormalizeBoundedText(
            settings.PublicName,
            MaxPublicNameLength,
            TenantDirectoryOperatorIdentityReasonCodes.MissingPublicName,
            TenantDirectoryOperatorIdentityReasonCodes.InvalidPublicName,
            reasons);
        string? legalName = NormalizeBoundedText(
            settings.LegalName,
            MaxLegalNameLength,
            TenantDirectoryOperatorIdentityReasonCodes.MissingLegalName,
            TenantDirectoryOperatorIdentityReasonCodes.InvalidLegalName,
            reasons);
        string? operatorKind = NormalizeOptionalText(settings.OperatorKindCode)?.ToLowerInvariant();
        if (operatorKind is null)
        {
            reasons.Add(TenantDirectoryOperatorIdentityReasonCodes.MissingOperatorKind);
        }
        else if (!TenantDirectoryOperatorKinds.All.Contains(operatorKind))
        {
            reasons.Add(TenantDirectoryOperatorIdentityReasonCodes.InvalidOperatorKind);
        }

        string? countryCode = NormalizeOptionalText(settings.JurisdictionCountryCode)?.ToUpperInvariant();
        if (countryCode is null)
        {
            reasons.Add(TenantDirectoryOperatorIdentityReasonCodes.MissingJurisdictionCountry);
        }
        else if (!IsIsoAlpha2Shape(countryCode))
        {
            reasons.Add(TenantDirectoryOperatorIdentityReasonCodes.InvalidJurisdictionCountry);
        }

        string? registrationIdentifier = NormalizeOptionalText(settings.RegistrationIdentifier);
        if (registrationIdentifier is not null
            && !IsBoundedPlainText(registrationIdentifier, MaxRegistrationIdentifierLength))
        {
            reasons.Add(TenantDirectoryOperatorIdentityReasonCodes.InvalidRegistrationIdentifier);
        }

        string? publicContactEmail = NormalizeEmail(settings.PublicContactEmail);
        if (string.IsNullOrWhiteSpace(settings.PublicContactEmail))
        {
            reasons.Add(TenantDirectoryOperatorIdentityReasonCodes.MissingPublicContactEmail);
        }
        else if (publicContactEmail is null)
        {
            reasons.Add(TenantDirectoryOperatorIdentityReasonCodes.InvalidPublicContactEmail);
        }

        string? legalNoticeUrl = NormalizeHttpsUrl(settings.LegalNoticeUrl);
        AddRequiredUrlReason(
            settings.LegalNoticeUrl,
            legalNoticeUrl,
            TenantDirectoryOperatorIdentityReasonCodes.MissingLegalNoticeUrl,
            TenantDirectoryOperatorIdentityReasonCodes.InvalidLegalNoticeUrl,
            reasons);

        string? termsUrl = NormalizeHttpsUrl(settings.TermsUrl);
        bool termsRequired = capability == TenantDirectoryOperatorIdentityCapability.PaidCommerce;
        if (termsRequired && string.IsNullOrWhiteSpace(settings.TermsUrl))
        {
            reasons.Add(TenantDirectoryOperatorIdentityReasonCodes.MissingTermsUrl);
        }
        else if (!string.IsNullOrWhiteSpace(settings.TermsUrl) && termsUrl is null)
        {
            reasons.Add(TenantDirectoryOperatorIdentityReasonCodes.InvalidTermsUrl);
        }

        string? privacyUrl = NormalizeHttpsUrl(settings.PrivacyUrl);
        AddRequiredUrlReason(
            settings.PrivacyUrl,
            privacyUrl,
            TenantDirectoryOperatorIdentityReasonCodes.MissingPrivacyUrl,
            TenantDirectoryOperatorIdentityReasonCodes.InvalidPrivacyUrl,
            reasons);

        if (reasons.Count > 0)
        {
            return new(false, null, reasons.ToImmutable());
        }

        return new(
            true,
            new TenantDirectoryOperatorIdentity(
                publicName!,
                legalName!,
                operatorKind!,
                countryCode!,
                registrationIdentifier,
                publicContactEmail!,
                legalNoticeUrl!,
                termsUrl,
                privacyUrl!),
            []);
    }

    private static void AddInvalidBoundedTextReason(
        string? value,
        int maxLength,
        string invalidReason,
        ImmutableArray<string>.Builder reasons)
    {
        if (value is not null && !IsBoundedPlainText(value, maxLength))
        {
            reasons.Add(invalidReason);
        }
    }

    private static void AddInvalidOptionalUrlReason(
        string? rawValue,
        string? normalizedValue,
        string invalidReason,
        ImmutableArray<string>.Builder reasons)
    {
        if (!string.IsNullOrWhiteSpace(rawValue) && normalizedValue is null)
        {
            reasons.Add(invalidReason);
        }
    }

    private static string? NormalizeBoundedText(
        string? value,
        int maxLength,
        string missingReason,
        string invalidReason,
        ImmutableArray<string>.Builder reasons)
    {
        string? normalized = NormalizeOptionalText(value);
        if (normalized is null)
        {
            reasons.Add(missingReason);
        }
        else if (!IsBoundedPlainText(normalized, maxLength))
        {
            reasons.Add(invalidReason);
        }

        return normalized;
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Normalize(NormalizationForm.FormC);

    private static bool IsBoundedPlainText(string value, int maxLength) =>
        value.Length <= maxLength && !value.Any(char.IsControl);

    private static bool IsIsoAlpha2Shape(string value) =>
        value.Length == 2
        && value.All(character => character is >= 'A' and <= 'Z');

    private static string? NormalizeEmail(string? value)
    {
        string? normalized = NormalizeOptionalText(value);
        if (normalized is null
            || normalized.Length > MaxPublicContactEmailLength
            || normalized.Any(char.IsControl)
            || !MailAddress.TryCreate(normalized, out MailAddress? parsed)
            || !string.Equals(parsed.Address, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalized.ToLowerInvariant();
    }

    private static string? NormalizeHttpsUrl(string? value)
    {
        string? normalized = NormalizeOptionalText(value);
        if (normalized is null)
        {
            return null;
        }

        if (normalized.Length > MaxLegalUrlLength
            || normalized.Any(char.IsControl)
            || !Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        return new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttps,
            Host = uri.IdnHost.ToLowerInvariant(),
            Port = uri.IsDefaultPort ? -1 : uri.Port
        }.Uri.AbsoluteUri;
    }

    private static void AddRequiredUrlReason(
        string? rawValue,
        string? normalizedValue,
        string missingReason,
        string invalidReason,
        ImmutableArray<string>.Builder reasons)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            reasons.Add(missingReason);
        }
        else if (normalizedValue is null)
        {
            reasons.Add(invalidReason);
        }
    }
}
