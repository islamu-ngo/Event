namespace Explore.Application.Contracts.Services;

public interface IInstanceOperatorIdentity
{
    Guid OperatorId { get; }
    string PublicName { get; }
    string LegalName { get; }
    bool IsOfficialInstance { get; }
    string OfficialOrigin { get; }
    string OperatorKindCode { get; }
    string JurisdictionCountryCode { get; }
    string? RegistrationIdentifier { get; }
    string PublicContactEmail { get; }
    string WebsiteUrl { get; }
    string LegalNoticeUrl { get; }
    string TermsUrl { get; }
    string PrivacyUrl { get; }
}
