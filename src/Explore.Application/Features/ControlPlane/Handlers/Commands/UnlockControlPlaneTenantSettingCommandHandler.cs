using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Handlers.Commands;

public sealed class UnlockControlPlaneTenantSettingCommandHandler(
    ITenantSettingRepository repository,
    ISystemSettingRepository systemSettingRepository,
    ISettingMutationLock mutationLock,
    ICurrentUserService currentUserService,
    IHierarchicalSettingsResolver settingsResolver,
    IMediator mediator)
    : IRequestHandler<UnlockControlPlaneTenantSettingCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        UnlockControlPlaneTenantSettingCommand request,
        CancellationToken cancellationToken)
    {
        Guid? actorUserId = currentUserService.UserId;
        if (actorUserId is null)
        {
            return ControlPlaneTenantSettingSecurity.Failure(
                request.TenantId,
                "authenticated_operator_required",
                "Authenticated operator context is required.");
        }

        BaseCommandResponse<Guid>? invalidTarget = ControlPlaneTenantSettingSecurity.ValidateTarget(
            request.TenantId,
            request.Key,
            out SettingDefinition definition);
        if (invalidTarget is not null)
        {
            return invalidTarget;
        }

        if (PublicationPolicySettingKeys.All.Contains(request.Key, StringComparer.Ordinal))
        {
            return ControlPlaneTenantSettingSecurity.Failure(
                request.TenantId,
                "setting_not_lockable",
                "The setting cannot be unlocked.");
        }

        if (!definition.IsLockable)
        {
            return ControlPlaneTenantSettingSecurity.Failure(
                request.TenantId,
                "setting_not_lockable",
                "The setting cannot be unlocked.");
        }

        (BaseCommandResponse<Guid> Response, SettingChangedNotification? Notification) outcome =
            await mutationLock.ExecuteAsync<(BaseCommandResponse<Guid>, SettingChangedNotification?)>(
            request.Key,
            async token =>
            {
                if (await systemSettingRepository.IsLocked(request.Key, token))
                {
                    return (ControlPlaneTenantSettingSecurity.Failure(
                        request.TenantId, "setting_system_locked", "The setting is locked at system scope."), null);
                }

                TenantSetting? existing = await repository.GetByTenantAndKey(
                    request.TenantId,
                    request.Key,
                    token);
                if (existing is null)
                {
                    return (ControlPlaneTenantSettingSecurity.Failure(
                        request.TenantId, "setting_override_not_found", "No tenant override exists for the setting."), null);
                }

                if (!existing.IsLocked)
                {
                    return (ControlPlaneTenantSettingSecurity.Failure(
                        request.TenantId, "setting_state_conflict", "The tenant setting is already unlocked."), null);
                }

                bool applied = await repository.UnlockAsync(
                    request.TenantId,
                    request.Key,
                    actorUserId.Value,
                    token);
                if (!applied)
                {
                    return (ControlPlaneTenantSettingSecurity.Failure(
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
            },
            cancellationToken);

        if (outcome.Notification is not null)
        {
            settingsResolver.InvalidateCache(SettingScope.Tenant, request.TenantId);
            await mediator.Publish(outcome.Notification, cancellationToken);
        }

        return outcome.Response;
    }
}
