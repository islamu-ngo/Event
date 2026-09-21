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
        string? termsUrl,
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
    public string? TermsUrl { get; }
    public string PrivacyUrl { get; }

    public static InstanceOperatorIdentity Create(InstanceOperatorIdentityOptions options)
    {
        (InstanceOperatorIdentity? identity, ImmutableArray<string> failures) = TryCreate(options, InstanceOperatorIdentityCapability.PaidCommerce);
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
        ImmutableArray<string> Failures) TryCreate(
            InstanceOperatorIdentityOptions options,
            InstanceOperatorIdentityCapability capability)
    {
        ArgumentNullException.ThrowIfNull(options);
        InstanceOperatorIdentityReadiness readiness = InstanceOperatorIdentityReadiness.Evaluate(
            new InstanceOperatorIdentitySettings
            {
                OperatorId = options.OperatorId,
                PublicName = options.PublicName,
                LegalName = options.LegalName,
                IsOfficialInstance = options.IsOfficialInstance,
                OfficialOrigin = options.OfficialOrigin,
                OperatorKindCode = options.OperatorKindCode,
                JurisdictionCountryCode = options.JurisdictionCountryCode,
                RegistrationIdentifier = options.RegistrationIdentifier,
                PublicContactEmail = options.PublicContactEmail,
                WebsiteUrl = options.WebsiteUrl,
                LegalNoticeUrl = options.LegalNoticeUrl,
                TermsUrl = options.TermsUrl,
                PrivacyUrl = options.PrivacyUrl
            }, capability);
        if (readiness.Normalized is not { } identity)
        {
            return (null, readiness.ReasonCodes);
        }

        return (
            new InstanceOperatorIdentity(
                identity.OperatorId!.Value,
                identity.PublicName!,
                identity.LegalName!,
                identity.IsOfficialInstance,
                identity.OfficialOrigin!,
                identity.OperatorKindCode!,
                identity.JurisdictionCountryCode!,
                identity.RegistrationIdentifier,
                identity.PublicContactEmail!,
                identity.WebsiteUrl!,
                identity.LegalNoticeUrl!,
                identity.TermsUrl,
                identity.PrivacyUrl!),
            []);
    }
}

public sealed class InstanceOperatorIdentityOptionsValidator :
    IValidateOptions<InstanceOperatorIdentityOptions>
{
    public ValidateOptionsResult Validate(string? name, InstanceOperatorIdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _ = name;
        (_, ImmutableArray<string> failures) = InstanceOperatorIdentity.TryCreate(options, InstanceOperatorIdentityCapability.PaidCommerce);
        return failures.IsEmpty
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
