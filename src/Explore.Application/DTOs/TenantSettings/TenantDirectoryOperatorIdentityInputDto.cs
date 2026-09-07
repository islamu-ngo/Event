namespace Explore.Application.DTOs.TenantSettings;

using Explore.Domain.Settings.Documents.Payloads;

public sealed record TenantDirectoryOperatorIdentityInputDto
{
    public string? PublicName { get; init; }
    public string? LegalName { get; init; }
    public string? OperatorKindCode { get; init; }
    public string? JurisdictionCountryCode { get; init; }
    public string? RegistrationIdentifier { get; init; }
    public string? PublicContactEmail { get; init; }
    public string? LegalNoticeUrl { get; init; }
    public string? TermsUrl { get; init; }
    public string? PrivacyUrl { get; init; }

    public TenantDirectoryOperatorIdentitySettings ToPayload() => new()
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
}
