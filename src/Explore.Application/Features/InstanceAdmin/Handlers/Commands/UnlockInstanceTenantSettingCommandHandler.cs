using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Features.Settings.Handlers;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Handlers.Commands;

public sealed class UnlockInstanceTenantSettingCommandHandler(
    ITenantSettingRepository repository,
    ISystemSettingRepository systemSettingRepository,
    ISettingMutationLock mutationLock,
    ICurrentUserService currentUserService,
    IHierarchicalSettingsResolver settingsResolver,
    IEnumerable<Contracts.Operations.INotificationHandler<SettingChangedNotification>> notificationHandlers,
    IEmailDeliverySettingsWriter emailDeliverySettingsWriter,
    IVisitorAccessSettingsWriter visitorSettings,
    IInstanceAdministratorCommitAuthority commitAuthority,
    IHumanCommitScopePreparation humanCommitScope,
    IHumanSettingsMutationPreparation settingsPreparation,
    IEventResourceSettingsWriter eventResourceSettings,
    IHumanEmailMutationPreparation emailPreparation)
    : ICommandHandler<UnlockInstanceTenantSettingCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        UnlockInstanceTenantSettingCommand request,
        CancellationToken cancellationToken)
    {
        await humanCommitScope.PrepareAsync(cancellationToken);
        Guid? actorUserId = currentUserService.UserId;
        if (actorUserId is null)
        {
            return InstanceTenantSettingSecurity.Failure(
                request.TenantId,
                "authenticated_operator_required",
                "Authenticated operator context is required.");
        }

        BaseCommandResponse<Guid>? invalidTarget = InstanceTenantSettingSecurity.ValidateTarget(
            request.TenantId,
            request.Key,
            out SettingDefinition definition);
        if (invalidTarget is not null)
        {
            return invalidTarget;
        }

        if (PublicationPolicySettingKeys.All.Contains(request.Key, StringComparer.Ordinal))
        {
            return InstanceTenantSettingSecurity.Failure(
                request.TenantId,
                "setting_not_lockable",
                "The setting cannot be unlocked.");
        }

        if (!definition.IsLockable)
        {
            return InstanceTenantSettingSecurity.Failure(
                request.TenantId,
                "setting_not_lockable",
                "The setting cannot be unlocked.");
        }

        bool specialized = EmailDeliverySettingKeys.Contains(request.Key);
        var prepared = specialized ? null : await settingsPreparation.PrepareAsync(request, cancellationToken);
        var email = specialized ? await emailPreparation.PrepareAsync(request, cancellationToken) : null;
        if (prepared is not null)
            SettingCommandHelper.RequireHumanSubject(humanCommitScope, actorUserId);
        (BaseCommandResponse<Guid> Response, SettingChangedNotification? Notification) outcome =
            specialized
                ? await emailPreparation.ExecutePreparedAsync(email!, ApplyAsync, cancellationToken)
                : await mutationLock.ExecuteOrderedGroupsAsync(
                    [VisitorAccessSettingMutationGuard.Handles(request.Key)
                        ? Explore.Application.Services.VisitorAccessCapabilityResolver.AuthoritySettingKeys : [],
                     EventResourceSettingMutationGuard.Handles(request.Key) ? EventResourceSettingMutationGuard.Keys : [],
                     [request.Key]], token => settingsPreparation.ExecutePreparedAsync(
                        prepared!, (_, ownerToken) => ApplyAsync(ownerToken), token), cancellationToken);

        if (outcome.Notification is not null)
        {
            settingsResolver.InvalidateCache(SettingScope.Tenant, request.TenantId);
            await notificationHandlers.HandleAsync(outcome.Notification, CancellationToken.None);
        }

        return outcome.Response;

        async Task<(BaseCommandResponse<Guid> Response, SettingChangedNotification? Notification)>
            ApplyAsync(CancellationToken token)
        {
            if (!await commitAuthority.IsInstanceAdministratorForCommitAsync(actorUserId.Value, token))
                return (BaseCommandResponse.Authorization<Guid>(
                    "Current instance administrator authority is required."), null);
            if (await systemSettingRepository.IsLocked(request.Key, token))
            {
                return (InstanceTenantSettingSecurity.Failure(
                    request.TenantId, "setting_system_locked", "The setting is locked at system scope."), null);
            }

            TenantSetting? existing = await repository.GetByTenantAndKey(
                request.TenantId,
                request.Key,
                token);
            if (existing is null)
            {
                return (InstanceTenantSettingSecurity.Failure(
                    request.TenantId, "setting_override_not_found", "No tenant override exists for the setting."), null);
            }

            if (!existing.IsLocked)
            {
                return (InstanceTenantSettingSecurity.Failure(
                    request.TenantId, "setting_state_conflict", "The tenant setting is already unlocked."), null);
            }

            if (VisitorAccessSettingMutationGuard.Handles(request.Key))
            {
                var result = await visitorSettings.ApplyAsync(
                    [new(request.TenantId, request.Key, VisitorAccessSettingMutationKind.SetLock, IsLocked: false)],
                    SettingCommandHelper.RequireHumanSubject(humanCommitScope, actorUserId),
                    SettingsMutationOperation.InstanceTenantSettings,
                    actorUserId, token);
                return (result.ToCommandResponse(request.TenantId), result.DeferredNotifications.SingleOrDefault());
            }

            if (EmailDeliverySettingKeys.Contains(request.Key))
            {
                EmailDeliverySettingsWriteResult result = await emailDeliverySettingsWriter.ApplyInstanceTenantAsync(
                    [new EmailDeliverySettingMutation(
                            TenantId: request.TenantId,
                            Key: request.Key,
                            Kind: EmailDeliverySettingMutationKind.SetLock,
                            IsLocked: false)],
                    actorUserId,
                    token);
                return (
                    result.ToCommandResponse(request.TenantId, "Tenant setting unlocked."),
                    result.IsAccepted() ? result.ToNotifications(actorUserId).SingleOrDefault() : null);
            }

            if (EventResourceSettingMutationGuard.Handles(request.Key))
            {
                var result = await eventResourceSettings.ApplyAsync(
                    [new(request.TenantId, request.Key, EventResourceSettingMutationKind.SetLock, IsLocked: false)],
                    SettingCommandHelper.RequireHumanSubject(humanCommitScope, actorUserId),
                    SettingsMutationOperation.InstanceTenantSettings, actorUserId, token);
                return (result.Success ? BaseCommandResponse.Success(request.TenantId, "Resource policy updated.")
                    : BaseCommandResponse.Failure<Guid>(result.FailureCode!,
                        "The resource policy change conflicts with the current instance ceilings or setting locks."),
                    result.DeferredNotifications.SingleOrDefault());
            }

            bool applied = await repository.UnlockAsync(
                request.TenantId,
                request.Key,
                actorUserId.Value,
                token);
            if (!applied)
            {
                return (InstanceTenantSettingSecurity.Failure(
                    request.TenantId, "setting_state_conflict", "The tenant setting state changed before it could be unlocked."), null);
            }

            BaseCommandResponse<Guid> response = BaseCommandResponse.Success(
                request.TenantId,
                "Tenant setting unlocked.");
            var notification = new SettingChangedNotification(
                request.Key,
                existing.Value,
                existing.Value,
                SettingSource.TenantOverride,
                request.TenantId,
                actorUserId,
                DateTime.UtcNow);
            return (response, notification);
        }
    }
}
