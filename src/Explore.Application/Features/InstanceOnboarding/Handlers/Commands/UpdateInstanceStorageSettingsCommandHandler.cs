using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Responses;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public class UpdateInstanceStorageSettingsCommandHandler : ICommandHandler<UpdateInstanceStorageSettingsCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IInstanceStorageSettingService _storageSettingService;

    public UpdateInstanceStorageSettingsCommandHandler(
        IAdminContext adminContext,
        IInstanceStorageSettingService storageSettingService)
    {
        _adminContext = adminContext;
        _storageSettingService = storageSettingService;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateInstanceStorageSettingsCommand request, CancellationToken cancellationToken)
    {
        var isInstanceAdmin = await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken);
        if (!isInstanceAdmin)
        {
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update storage settings.");
        }

        if (!request.Patch.HasChanges()
            || request.Patch.Policy is { HasValue: true, Value: null }
            || request.Patch.S3Configuration is { HasValue: true, Value: null })
        {
            const string message = "Storage settings patch must include a complete policy or S3 configuration group.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        await using var observation = await _storageSettingService.ObserveConfigurationAsync(request, cancellationToken);
        var settings = observation.InstanceProjection;

        var validator = new InstanceStorageSettingsDtoValidator();
        var validationResult = await validator.ValidateAsync(settings, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage),
                "Storage settings validation failed.");
        }

        await _storageSettingService.ApplySettingsAsync(observation, cancellationToken);

        return BaseCommandResponse.Success(Guid.Empty, "Storage settings updated successfully.");
    }
}
