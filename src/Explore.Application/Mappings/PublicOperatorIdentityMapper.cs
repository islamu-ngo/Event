using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Contracts.Services;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Mappings;

internal static class PublicOperatorIdentityMapper
{
    internal static TenantDirectoryOperatorPublicDto MapDirectoryOperator(
        TenantDirectoryOperatorIdentity identity, Guid documentRevision) => new()
        {
            DocumentRevision = documentRevision,
            PublicName = identity.PublicName,
            LegalName = identity.LegalName,
            OperatorKindCode = identity.OperatorKindCode,
            JurisdictionCountryCode = identity.JurisdictionCountryCode,
            RegistrationIdentifier = identity.RegistrationIdentifier,
            PublicContactEmail = identity.PublicContactEmail,
            LegalNoticeUrl = identity.LegalNoticeUrl,
            TermsUrl = identity.TermsUrl,
            PrivacyUrl = identity.PrivacyUrl
        };

    internal static InstanceOperatorPublicDto MapInstanceOperator(IInstanceOperatorIdentity identity) => new()
    {
        OperatorId = identity.OperatorId,
        PublicName = identity.PublicName,
        LegalName = identity.LegalName,
        IsOfficialInstance = identity.IsOfficialInstance,
        OfficialOrigin = identity.OfficialOrigin,
        OperatorKindCode = identity.OperatorKindCode,
        JurisdictionCountryCode = identity.JurisdictionCountryCode,
        RegistrationIdentifier = identity.RegistrationIdentifier,
        PublicContactEmail = identity.PublicContactEmail,
        WebsiteUrl = identity.WebsiteUrl,
        LegalNoticeUrl = identity.LegalNoticeUrl,
        TermsUrl = identity.TermsUrl ?? string.Empty,
        PrivacyUrl = identity.PrivacyUrl
    };
}
