using System.Globalization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Integrations;
using Explore.Application.DTOs.Integrations.Validators;
using Explore.Application.Features.Integrations.Listmonk.Requests.Commands;
using Explore.Application.Features.Settings.Handlers;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using MediatR;

namespace Explore.Application.Features.Integrations.Listmonk.Handlers.Commands;

public sealed class UpdateListmonkIntegrationSettingsCommandHandler(
    IHierarchicalSettingsResolver settingsResolver,
    IAdminContext adminContext,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    IPublisher publisher)
    : IRequestHandler<UpdateListmonkIntegrationSettingsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdateListmonkIntegrationSettingsCommand request,
        CancellationToken cancellationToken)
    {
        var authorization = await SettingCommandHelper.CheckAuthorizationAsync(
            SettingScope.Tenant,
            adminContext,
            tenantContext,
            currentUserService,
            cancellationToken);
        if (!authorization.Authorized)
        {
            return BaseCommandResponse.Authorization<Guid>(authorization.Error);
        }

        var actorId = await SettingCommandHelper.ResolveCurrentUserIdAsync(
            adminContext,
            currentUserService,
            cancellationToken);
        if (!actorId.HasValue)
        {
            return BaseCommandResponse.Authentication<Guid>(
                "Authentication is required to update Listmonk integration settings.");
        }

        var context = SettingCommandHelper.BuildSettingContext(SettingScope.Tenant, tenantContext, actorId.Value);
        var currentDto = new ListmonkIntegrationSettingsDto
        {
            Enabled = await settingsResolver.ResolveAsync<bool>(GovernanceSettingKeys.Integrations.Listmonk.Enabled, context, cancellationToken),
            InstanceUrl = await settingsResolver.ResolveAsync<string>(GovernanceSettingKeys.Integrations.Listmonk.InstanceUrl, context, cancellationToken),
            DefaultListId = await settingsResolver.ResolveAsync<int>(GovernanceSettingKeys.Integrations.Listmonk.DefaultListId, context, cancellationToken),
            PreconfirmSubscriptions = await settingsResolver.ResolveAsync<bool>(GovernanceSettingKeys.Integrations.Listmonk.PreconfirmSubscriptions, context, cancellationToken),
            SyncOnRegistration = await settingsResolver.ResolveAsync<bool>(GovernanceSettingKeys.Integrations.Listmonk.SyncOnRegistration, context, cancellationToken)
        };
        var validator = new UpdateListmonkIntegrationSettingsDtoValidator(currentDto);
        var validation = await validator.ValidateAsync(request.Dto, cancellationToken);
        if (!validation.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validation.Errors.Select(e => e.ErrorMessage),
                "Listmonk integration settings update failed.");
        }

        var values = Values(request.Dto);
        var keys = values.Keys.ToArray();
        var currentSettings = (await settingsResolver.ResolveBatchAsync(keys, context, cancellationToken))
            .ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, plainValue) in values)
        {
            var definition = SettingRegistry.Get(key);
            if (definition is null)
            {
                return BaseCommandResponse.Validation<Guid>(
                    [$"Unknown setting '{key}'."],
                    "Listmonk integration settings update failed.");
            }

            currentSettings.TryGetValue(key, out var current);
            var lockState = current is null
                ? (IsBlockedByLock: false, LockReason: (string?)null)
                : SettingCommandHelper.CheckLockState(current, SettingScope.Tenant);
            if (lockState.IsBlockedByLock)
            {
                return BaseCommandResponse.Validation<Guid>(
                    [lockState.LockReason ?? "Listmonk integration settings are locked."],
                    "Listmonk integration settings update failed.");
            }

            var serialized = SettingCommandHelper.ValidateAndSerialize(plainValue, definition);
            if (!serialized.IsValid)
            {
                return BaseCommandResponse.Validation<Guid>(
                    [serialized.Error ?? $"Invalid value for '{key}'."],
                    "Listmonk integration settings update failed.");
            }

            await settingsResolver.SetValueAsync(
                key,
                serialized.SerializedValue!,
                SettingScope.Tenant,
                tenantContext.TenantId,
                actorId.Value,
                cancellationToken);
            await publisher.Publish(
                new SettingChangedNotification(
                    key,
                    current?.Value,
                    serialized.SerializedValue,
                    SettingCommandHelper.MapScopeToSource(SettingScope.Tenant),
                    tenantContext.TenantId,
                    actorId.Value,
                    DateTime.UtcNow),
                cancellationToken);
        }

        return BaseCommandResponse.Success(
            tenantContext.TenantId,
            "Listmonk integration settings updated successfully.");
    }

    private static Dictionary<string, string> Values(UpdateListmonkIntegrationSettingsDto dto)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (dto.Connection is { } connection)
        {
            values[GovernanceSettingKeys.Integrations.Listmonk.InstanceUrl] = connection.InstanceUrl?.Trim() ?? string.Empty;
            values[GovernanceSettingKeys.Integrations.Listmonk.DefaultListId] = connection.DefaultListId.ToString(CultureInfo.InvariantCulture);
        }

        if (dto.Behavior is { } behavior)
        {
            values[GovernanceSettingKeys.Integrations.Listmonk.Enabled] = behavior.Enabled.ToString(CultureInfo.InvariantCulture);
            values[GovernanceSettingKeys.Integrations.Listmonk.PreconfirmSubscriptions] = behavior.PreconfirmSubscriptions.ToString(CultureInfo.InvariantCulture);
            values[GovernanceSettingKeys.Integrations.Listmonk.SyncOnRegistration] = behavior.SyncOnRegistration.ToString(CultureInfo.InvariantCulture);
        }

        return values;
    }
}
