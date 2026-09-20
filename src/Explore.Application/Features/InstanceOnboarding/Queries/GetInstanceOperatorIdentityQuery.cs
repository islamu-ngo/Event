namespace Explore.Application.Features.InstanceOnboarding.Queries;

using Explore.Application.DTOs.Onboarding;
using Explore.Application.Services;
using Explore.Application.Contracts.Operations;

/// <summary>
/// Retrieves the current instance operator identity document and readiness assessment.
/// </summary>
public sealed record GetInstanceOperatorIdentityQuery : IQuery<InstanceOperatorIdentityDocumentDto>;

/// <summary>
/// Handler for <see cref="GetInstanceOperatorIdentityQuery"/>.
/// </summary>
public sealed class GetInstanceOperatorIdentityQueryHandler(
    InstanceOperatorIdentityService identityService)
    : IQueryHandler<GetInstanceOperatorIdentityQuery, InstanceOperatorIdentityDocumentDto>
{
    public async Task<InstanceOperatorIdentityDocumentDto> QueryAsync(
        GetInstanceOperatorIdentityQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        InstanceOperatorIdentityDocument doc = await identityService.GetCurrentAsync(cancellationToken);

        return new InstanceOperatorIdentityDocumentDto
        {
            IsReady = doc.Readiness.IsReady,
            FailureCode = doc.Readiness.FailureCode,
            ReasonCodes = doc.Readiness.ReasonCodes,
            Revision = doc.Readiness.DocumentRevision,
            OperatorId = doc.Settings?.OperatorId,
            PublicName = doc.Settings?.PublicName,
            LegalName = doc.Settings?.LegalName,
            OperatorKindCode = doc.Settings?.OperatorKindCode,
            JurisdictionCountryCode = doc.Settings?.JurisdictionCountryCode,
            RegistrationIdentifier = doc.Settings?.RegistrationIdentifier,
            PublicContactEmail = doc.Settings?.PublicContactEmail,
            WebsiteUrl = doc.Settings?.WebsiteUrl,
            LegalNoticeUrl = doc.Settings?.LegalNoticeUrl,
            TermsUrl = doc.Settings?.TermsUrl,
            PrivacyUrl = doc.Settings?.PrivacyUrl,
            IsOfficialInstance = doc.Settings?.IsOfficialInstance ?? false,
            OfficialOrigin = doc.Settings?.OfficialOrigin
        };
    }
}
