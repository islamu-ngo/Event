using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.PlatformMonetization.Requests.Commands;
using Explore.Application.Features.PlatformMonetization.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.PlatformMonetization.Handlers.Commands;

public sealed class UpdatePlatformMonetizationSettingsCommandHandler(
    IAdminContext adminContext,
    IPlatformFeePolicyRepository feePolicies,
    IPlatformContributionSettingRepository contributions,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdatePlatformMonetizationSettingsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdatePlatformMonetizationSettingsCommand request,
        CancellationToken cancellationToken)
    {
        if (!await adminContext.IsInstanceAdminAsync(cancellationToken))
        {
            throw new AuthorizationException(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update);
        }

        var validation = await new UpdatePlatformMonetizationSettingsValidator().ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation);
        }

        Guid revisionId = await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            PlatformFeePolicy? fee = await feePolicies.GetActiveAsync(token);
            PlatformContributionSetting? contribution = await contributions.GetActiveAsync(token);
            if (fee is null)
            {
                throw new NotFoundException(nameof(PlatformFeePolicy), "active");
            }

            if (contribution is null)
            {
                throw new NotFoundException(nameof(PlatformContributionSetting), "active");
            }

            if (fee.VersionNumber != request.Settings.ExpectedFeeVersion)
            {
                throw new ConcurrencyConflictException(
                    ConcurrencyConflictException.ConcurrentUpdate,
                    "The platform fee policy was updated by another request.",
                    nameof(PlatformFeePolicy),
                    fee.Id.ToString());
            }

            if (contribution.VersionNumber != request.Settings.ExpectedContributionVersion)
            {
                throw new ConcurrencyConflictException(
                    ConcurrencyConflictException.ConcurrentUpdate,
                    "The platform contribution setting was updated by another request.",
                    nameof(PlatformContributionSetting),
                    contribution.Id.ToString());
            }

            PlatformFeePolicy feeRevision = fee.CreateRevision(
                request.Settings.FeeEnabled,
                request.Settings.FeeBasisPoints,
                request.Settings.FixedCharges.Select(charge =>
                    PlatformFeeFixedCharge.Create(charge.CurrencyCode, charge.AmountMinor)));
            PlatformContributionSetting contributionRevision = contribution.CreateRevision(
                request.Settings.ContributionEnabled,
                request.Settings.ContributionHeading,
                request.Settings.ContributionBody,
                request.Settings.ContributionOptions.Select(option =>
                    PlatformContributionOption.Create(option.ContributionBasisPoints, option.SortOrder, option.IsDefault)));

            await feePolicies.UpdateAsync(fee, token);
            await contributions.UpdateAsync(contribution, token);
            await feePolicies.AddAsync(feeRevision, token);
            await contributions.AddAsync(contributionRevision, token);
            return feeRevision.Id;
        }, cancellationToken);

        return BaseCommandResponse.Success(revisionId, "Platform monetization settings updated.");
    }
}
