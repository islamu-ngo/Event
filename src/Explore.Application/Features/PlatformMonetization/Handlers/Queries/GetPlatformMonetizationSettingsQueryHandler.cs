using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.PlatformMonetization;
using Explore.Application.Exceptions;
using Explore.Application.Features.PlatformMonetization.Requests.Queries;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.PlatformMonetization.Handlers.Queries;

public sealed class GetPlatformMonetizationSettingsQueryHandler(
    IAdminContext adminContext,
    IPlatformFeePolicyRepository feePolicies,
    IPlatformContributionSettingRepository contributions)
    : IRequestHandler<GetPlatformMonetizationSettingsQuery, PlatformMonetizationSettingsDto>
{
    public async Task<PlatformMonetizationSettingsDto> Handle(
        GetPlatformMonetizationSettingsQuery request,
        CancellationToken cancellationToken)
    {
        if (!await adminContext.IsInstanceAdminAsync(cancellationToken))
        {
            throw new AuthorizationException(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View);
        }

        PlatformFeePolicy? fee = await feePolicies.GetActiveAsync(cancellationToken);
        PlatformContributionSetting? contribution = await contributions.GetActiveAsync(cancellationToken);
        if (fee is null)
        {
            throw new NotFoundException(nameof(PlatformFeePolicy), "active");
        }

        if (contribution is null)
        {
            throw new NotFoundException(nameof(PlatformContributionSetting), "active");
        }

        return new PlatformMonetizationSettingsDto
        {
            FeeEnabled = fee.IsEnabled,
            FeeBasisPoints = fee.FeeBasisPoints,
            FixedCharges = fee.FixedCharges
                .OrderBy(charge => charge.CurrencyCode, StringComparer.Ordinal)
                .Select(charge => new PlatformFeeFixedChargeDto
                {
                    CurrencyCode = charge.CurrencyCode,
                    AmountMinor = charge.AmountMinor
                })
                .ToArray(),
            FeeVersion = fee.VersionNumber,
            ContributionEnabled = contribution.IsEnabled,
            ContributionHeading = contribution.Heading,
            ContributionBody = contribution.Body,
            ContributionOptions = contribution.Options
                .OrderBy(option => option.SortOrder)
                .ThenBy(option => option.ContributionBasisPoints)
                .Select(option => new PlatformContributionOptionDto
                {
                    ContributionBasisPoints = option.ContributionBasisPoints,
                    SortOrder = option.SortOrder,
                    IsDefault = option.IsDefault
                })
                .ToArray(),
            ContributionVersion = contribution.VersionNumber
        };
    }
}
