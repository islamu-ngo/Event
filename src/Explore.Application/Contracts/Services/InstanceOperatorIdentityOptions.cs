namespace Explore.Application.Contracts.Services;

using System.Collections.Immutable;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;
using Microsoft.Extensions.Options;

public sealed class InstanceOperatorIdentityOptions
{
    public const string SectionName = "Instance:OperatorIdentity";

    public Guid OperatorId { get; set; }
    public string PublicName { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public bool IsOfficialInstance { get; set; }
    public string OfficialOrigin { get; set; } = string.Empty;
    public string OperatorKindCode { get; set; } = string.Empty;
    public string JurisdictionCountryCode { get; set; } = string.Empty;
    public string? RegistrationIdentifier { get; set; }
    public string PublicContactEmail { get; set; } = string.Empty;
    public string WebsiteUrl { get; set; } = string.Empty;
    public string LegalNoticeUrl { get; set; } = string.Empty;
    public string TermsUrl { get; set; } = string.Empty;
    public string PrivacyUrl { get; set; } = string.Empty;
}

public sealed record InstanceOperatorIdentity : IInstanceOperatorIdentity
{
    private const string TenantReasonPrefix = "tenant_directory_operator_identity_";
    private const string InstanceReasonPrefix = "instance_operator_identity_";

    private InstanceOperatorIdentity(
        Guid operatorId,
        string publicName,
        string legalName,
        bool isOfficialInstance,
        string officialOrigin,
        string operatorKindCode,
        string jurisdictionCountryCode,
        string? registrationIdentifier,
        string publicContactEmail,
        string websiteUrl,
        string legalNoticeUrl,
        string termsUrl,
        string privacyUrl)
    {
        OperatorId = operatorId;
        PublicName = publicName;
        LegalName = legalName;
        IsOfficialInstance = isOfficialInstance;
        OfficialOrigin = officialOrigin;
        OperatorKindCode = operatorKindCode;
        JurisdictionCountryCode = jurisdictionCountryCode;
        RegistrationIdentifier = registrationIdentifier;
        PublicContactEmail = publicContactEmail;
        WebsiteUrl = websiteUrl;
        LegalNoticeUrl = legalNoticeUrl;
        TermsUrl = termsUrl;
        PrivacyUrl = privacyUrl;
    }

    public Guid OperatorId { get; }
    public string PublicName { get; }
    public string LegalName { get; }
    public bool IsOfficialInstance { get; }
    public string OfficialOrigin { get; }
    public string OperatorKindCode { get; }
    public string JurisdictionCountryCode { get; }
    public string? RegistrationIdentifier { get; }
    public string PublicContactEmail { get; }
    public string WebsiteUrl { get; }
    public string LegalNoticeUrl { get; }
    public string TermsUrl { get; }
    public string PrivacyUrl { get; }

    public static InstanceOperatorIdentity Create(InstanceOperatorIdentityOptions options)
    {
        (InstanceOperatorIdentity? identity, ImmutableArray<string> failures) = TryCreate(options);
        if (identity is null)
        {
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(InstanceOperatorIdentityOptions),
                failures);
        }

        return identity;
    }

    internal static (
        InstanceOperatorIdentity? Identity,
        ImmutableArray<string> Failures) TryCreate(InstanceOperatorIdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = ImmutableArray.CreateBuilder<string>();
        if (options.OperatorId == Guid.Empty || options.OperatorId.Version != 7)
        {
            failures.Add("instance_operator_identity_operator_id_invalid");
        }

        TenantDirectoryOperatorIdentityReadiness readiness =
            TenantDirectoryOperatorIdentity.Evaluate(
                new TenantDirectoryOperatorIdentitySettings
                {
                    PublicName = options.PublicName,
                    LegalName = options.LegalName,
                    OperatorKindCode = options.OperatorKindCode,
                    JurisdictionCountryCode = options.JurisdictionCountryCode,
                    RegistrationIdentifier = options.RegistrationIdentifier,
                    PublicContactEmail = options.PublicContactEmail,
                    LegalNoticeUrl = options.LegalNoticeUrl,
                    TermsUrl = options.TermsUrl,
                    PrivacyUrl = options.PrivacyUrl
                },
                TenantDirectoryOperatorIdentityCapability.PaidCommerce);
        failures.AddRange(readiness.ReasonCodes.Select(MapReasonCode));

        string? officialOrigin = NormalizeOfficialOrigin(options.OfficialOrigin);
        if (officialOrigin is null)
        {
            failures.Add(
                string.IsNullOrWhiteSpace(options.OfficialOrigin)
                    ? "instance_operator_identity_official_origin_missing"
                    : "instance_operator_identity_official_origin_invalid");
        }

        string? websiteUrl = NormalizeHttpsUrl(options.WebsiteUrl);
        if (websiteUrl is null)
        {
            failures.Add(
                string.IsNullOrWhiteSpace(options.WebsiteUrl)
                    ? "instance_operator_identity_website_url_missing"
                    : "instance_operator_identity_website_url_invalid");
        }

        if (failures.Count > 0 || readiness.Identity is null)
        {
            return (null, failures.ToImmutable());
        }

        TenantDirectoryOperatorIdentity legalIdentity = readiness.Identity;
        return (
            new InstanceOperatorIdentity(
                options.OperatorId,
                legalIdentity.PublicName,
                legalIdentity.LegalName,
                options.IsOfficialInstance,
                officialOrigin!,
                legalIdentity.OperatorKindCode,
                legalIdentity.JurisdictionCountryCode,
                legalIdentity.RegistrationIdentifier,
                legalIdentity.PublicContactEmail,
                websiteUrl!,
                legalIdentity.LegalNoticeUrl,
                legalIdentity.TermsUrl!,
                legalIdentity.PrivacyUrl),
            []);
    }

    private static string MapReasonCode(string reasonCode) =>
        reasonCode.StartsWith(TenantReasonPrefix, StringComparison.Ordinal)
            ? string.Concat(InstanceReasonPrefix, reasonCode.AsSpan(TenantReasonPrefix.Length))
            : reasonCode;

    private static string? NormalizeOfficialOrigin(string? value)
    {
        string? normalized = NormalizeHttpsUrl(value);
        if (normalized is null
            || !Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static string? NormalizeHttpsUrl(string? value)
    {
        try
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : ExternalActionUrl.Create(value).Value;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

public sealed class InstanceOperatorIdentityOptionsValidator :
    IValidateOptions<InstanceOperatorIdentityOptions>
{
    public ValidateOptionsResult Validate(string? name, InstanceOperatorIdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _ = name;
        (_, ImmutableArray<string> failures) = InstanceOperatorIdentity.TryCreate(options);
        return failures.IsEmpty
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
