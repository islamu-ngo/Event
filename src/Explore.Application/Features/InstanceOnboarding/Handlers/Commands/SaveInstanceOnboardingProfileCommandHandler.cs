using System.Linq;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Features.InstanceOnboarding.Common;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Onboarding;
using Explore.Application.Responses;
using Explore.Domain.Enums;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public sealed class SaveInstanceOnboardingProfileCommandHandler(
    IInstanceBootstrapStateRepository instanceBootstrapStateRepository,
    ISystemSettingRepository systemSettingRepository,
    ISetupSecretProvider setupSecretProvider,
    IInstanceBootstrapAuditLogger instanceBootstrapAuditLogger,
    IUnitOfWork unitOfWork)
    : ICommandHandler<SaveInstanceOnboardingProfileCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        SaveInstanceOnboardingProfileCommand request,
        CancellationToken cancellationToken)
    {
        var bootstrap = await instanceBootstrapStateRepository.GetCurrent(cancellationToken);
        if (!await setupSecretProvider.IsSetupModeActiveAsync(cancellationToken))
        {
            const string message = "Setup mode is no longer active.";
            return BaseCommandResponse.Validation(
                [message],
                message,
                bootstrap?.Id ?? Guid.Empty);
        }

        var validator = new SelfHostOnboardingProfileDtoValidator();
        var validation = await validator.ValidateAsync(request.Profile, cancellationToken);
        if (!validation.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validation.Errors.Select(error => error.ErrorMessage),
                "Invalid onboarding profile.");
        }

        var profile = InstanceOnboardingProfileSettingHelpers.Normalize(request.Profile);
        var response = await unitOfWork.ExecuteBootstrapConvergenceAsync(async transactionToken =>
        {
            var current = await instanceBootstrapStateRepository.GetCurrentForUpdate(transactionToken);
            if (current?.Status == InstanceBootstrapStatus.Completed
                || !await setupSecretProvider.IsSetupModeActiveAsync(transactionToken))
                return BaseCommandResponse.Validation<Guid>(["Setup mode is no longer active."], "Setup mode is no longer active.");
            await InstanceOnboardingProfileSettingHelpers.PersistAsync(systemSettingRepository, profile, transactionToken);
            return BaseCommandResponse.Success(current?.Id ?? Guid.Empty, "Instance onboarding profile saved successfully.");
        }, cancellationToken);
        if (!response.IsSuccess) return response;

        instanceBootstrapAuditLogger.Log(new InstanceBootstrapAuditEvent(
            InstanceBootstrapAuditEventType.SetupProfileSaved,
            Operation: "instance_onboarding_profile_save",
            Outcome: "saved"));

        return response;
    }
}
