using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Features.InstanceOnboarding.Common;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Onboarding;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Explore.Application.Configuration;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public sealed class SaveInstanceOnboardingProfileCommandHandler(
    IInstanceBootstrapStateRepository instanceBootstrapStateRepository,
    ISystemSettingRepository systemSettingRepository,
    ISetupSecretProvider setupSecretProvider,
    IInstanceBootstrapAuditLogger instanceBootstrapAuditLogger,
    IUnitOfWork unitOfWork,
    IConfiguration configuration,
    ITrustedSetupSettingsWriter? setupSettingsWriter = null)
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
        var configuredUrl = PublicAddressResolver.ReadOverride(configuration);
        var profile = InstanceOnboardingProfileSettingHelpers.Normalize(request.Profile) with
        {
            PublicUrl = PublicAddressResolver.IsValid(configuredUrl) ? configuredUrl : request.Profile.PublicUrl
        };
        var validation = await validator.ValidateAsync(profile, cancellationToken);
        if (!validation.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validation.Errors.Select(error => error.ErrorMessage),
                "Invalid onboarding profile.");
        }

        var response = await unitOfWork.ExecuteBootstrapConvergenceAsync(async transactionToken =>
        {
            var current = await instanceBootstrapStateRepository.GetCurrentForUpdate(transactionToken);
            if (current?.Status == InstanceBootstrapStatus.Completed
                || !await setupSecretProvider.IsSetupModeActiveAsync(transactionToken))
                return BaseCommandResponse.Validation<Guid>(["Setup mode is no longer active."], "Setup mode is no longer active.");
            var writes = new List<SystemSetting>();
            InstanceOnboardingProfileSettingHelpers.AddSettings(writes, profile, configuration);
            Guid? bootstrapId = current?.Id;
            long? generation = current?.Generation;
            InstanceBootstrapStatus? status = current?.Status;
            InstanceBootstrapMode? mode = current?.Mode;
            string? fingerprint = current?.ConfigurationFingerprint;
            using var apply = new TrustedSetupSettingsApply<BaseCommandResponse<Guid>>(
                unitOfWork, new TrustedSetupSettingsSource(null, null, null), writes,
                (_, _) => Task.FromResult(BaseCommandResponse.Success(current?.Id ?? Guid.Empty,
                    "Instance onboarding profile saved successfully.")),
                async token =>
                {
                    var terminal = await instanceBootstrapStateRepository.GetCurrentForUpdate(token);
                    if (terminal?.Id != bootstrapId || terminal?.Generation != generation
                        || terminal?.Status != status || terminal?.Mode != mode
                        || terminal?.ConfigurationFingerprint != fingerprint)
                        throw new UnauthorizedAccessException("Setup profile lost its original pending generation.");
                    foreach (var effect in writes)
                    {
                        var stored = await systemSettingRepository.GetByKey(effect.SettingKey, token);
                        if (stored?.Value != effect.Value || stored.IsLocked != effect.IsLocked)
                            throw new UnauthorizedAccessException("Setup profile did not retain its exact effects.");
                    }
                });
            return await (setupSettingsWriter
                ?? throw new InvalidOperationException("Setup profile requires its trusted settings writer."))
                .ExecuteAsync(apply, transactionToken);
        }, cancellationToken);
        if (!response.IsSuccess) return response;

        instanceBootstrapAuditLogger.Log(new InstanceBootstrapAuditEvent(
            InstanceBootstrapAuditEventType.SetupProfileSaved,
            Operation: "instance_onboarding_profile_save",
            Outcome: "saved"));

        return response;
    }
}
