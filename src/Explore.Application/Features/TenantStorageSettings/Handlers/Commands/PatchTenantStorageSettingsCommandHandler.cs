using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Tenant.Validators;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TenantStorageSettings.Handlers.Commands;

public sealed class PatchTenantStorageSettingsCommandHandler
    : ICommandHandler<PatchTenantStorageSettingsCommand, BaseCommandResponse<Guid>>
{
    private const string LockedFailureCode = "StorageTenantOverridesLocked";

    private readonly ITenantContext _tenantContext;
    private readonly IAdminContext _adminContext;
    private readonly ITenantStorageSettingService _storageSettingService;

    public PatchTenantStorageSettingsCommandHandler(
        ITenantContext tenantContext,
        IAdminContext adminContext,
        ITenantStorageSettingService storageSettingService)
    {
        _tenantContext = tenantContext;
        _adminContext = adminContext;
        _storageSettingService = storageSettingService;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        PatchTenantStorageSettingsCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        if (!await IsUserAuthorizedAsync(tenantId, request.UserId, cancellationToken))
        {
            return BaseCommandResponse.Authorization<Guid>(
                "Only tenant administrators or instance administrators can patch tenant storage settings.");
        }

        var patchValidator = new PatchTenantStorageSettingsDtoValidator();
        var validationResult = await patchValidator.ValidateAsync(request.Settings, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage),
                "Tenant storage settings validation failed.");
        }

        await using var observation = await _storageSettingService.ObserveConfigurationAsync(request, cancellationToken);
        var currentSettings = observation.TenantProjection;
        if (currentSettings.IsReadOnly)
        {
            return BaseCommandResponse.Failure<Guid>(
                LockedFailureCode,
                "Tenant storage settings are locked by instance policy.",
                ["Storage delegation must be unlocked by an instance administrator before tenant overrides can be saved."]);
        }
        var settingsValidator = new TenantStorageSettingsDtoValidator(currentSettings.EffectivePolicy.InstanceMaxUploadBytes);
        validationResult = await settingsValidator.ValidateAsync(currentSettings, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage),
                "Tenant storage settings validation failed.");
        }

        await _storageSettingService.ApplyPatchAsync(observation, cancellationToken);

        return BaseCommandResponse.Success(tenantId, "Tenant storage settings patched successfully.");
    }

    private async Task<bool> IsUserAuthorizedAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken))
        {
            return true;
        }

        var adminTenantIds = await _adminContext.GetAdminTenantIdsAsync(userId, cancellationToken);
        if (adminTenantIds.Contains(tenantId))
        {
            return true;
        }

        return await _adminContext.IsInstanceAdminAsync(userId, cancellationToken);
    }
}
