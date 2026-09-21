using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Features.InstanceOnboarding.Common;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Responses;
using Explore.Domain.Enums;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public sealed class CompleteInstanceOnboardingCommandHandler(
    IInstanceBootstrapStateRepository bootstrapRepository,
    IUserRepository userRepository,
    IDeploymentModeProvider deploymentModeProvider,
    InstanceOnboardingCompletionOperation completionOperation)
    : ICommandHandler<CompleteInstanceOnboardingCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        CompleteInstanceOnboardingCommand request,
        CancellationToken cancellationToken)
    {
        var bootstrap = await bootstrapRepository.GetCurrent(cancellationToken);
        if (bootstrap?.Status == InstanceBootstrapStatus.Completed)
        {
            const string message = "Instance onboarding has already been completed.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        DeploymentMode deploymentMode =
            await deploymentModeProvider.GetConfiguredOnboardingModeAsync(cancellationToken);
        request.Settings.DeploymentMode = deploymentMode;
        if (string.IsNullOrWhiteSpace(request.Settings.SiteProfile.SiteName)
            && !string.IsNullOrWhiteSpace(request.Settings.InstanceName))
        {
            request.Settings.SiteProfile.SiteName = request.Settings.InstanceName;
        }

        var validator = new CompleteInstanceOnboardingRequestValidator();
        var validation = await validator.ValidateAsync(request.Settings, cancellationToken);
        if (!validation.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validation.Errors.Select(error => error.ErrorMessage),
                "Invalid onboarding request.");
        }

        var existingUser = await userRepository.GetById(request.UserId);
        if (existingUser is null && string.IsNullOrWhiteSpace(request.Email))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["No user found and no email claim available to create one."],
                "User identity data is required to complete onboarding.");
        }

        var siteProfile = InstanceOnboardingProfileSettingHelpers.Normalize(
            request.Settings.SiteProfile,
            request.Settings.InstanceName);
        return await completionOperation.CompleteInteractiveAsync(
            request,
            deploymentMode,
            siteProfile,
            cancellationToken);
    }
}
