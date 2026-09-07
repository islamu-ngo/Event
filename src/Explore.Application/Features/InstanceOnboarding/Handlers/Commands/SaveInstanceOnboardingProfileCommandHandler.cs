using System.Linq;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Features.InstanceOnboarding.Common;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Onboarding;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public sealed class SaveInstanceOnboardingProfileCommandHandler(
    IInstanceBootstrapStateRepository instanceBootstrapStateRepository,
    ISystemSettingRepository systemSettingRepository,
    ISetupSecretProvider setupSecretProvider,
    IInstanceBootstrapAuditLogger instanceBootstrapAuditLogger,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SaveInstanceOnboardingProfileCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
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
        await unitOfWork.ExecuteInTransactionAsync(
            async transactionToken => await InstanceOnboardingProfileSettingHelpers.PersistAsync(
                systemSettingRepository,
                profile,
                transactionToken),
            cancellationToken);

        instanceBootstrapAuditLogger.Log(new InstanceBootstrapAuditEvent(
            InstanceBootstrapAuditEventType.SetupProfileSaved,
            Operation: "instance_onboarding_profile_save",
            Outcome: "saved"));

        return BaseCommandResponse.Success(
            bootstrap?.Id ?? Guid.Empty,
            "Instance onboarding profile saved successfully.");
    }
}
