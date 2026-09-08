using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Handlers.Commands;

public sealed class LockControlPlaneTenantSettingCommandHandler(
    ITenantSettingRepository repository,
    ISystemSettingRepository systemSettingRepository,
    ISettingMutationLock mutationLock,
    ICurrentUserService currentUserService,
    IHierarchicalSettingsResolver settingsResolver,
    IMediator mediator,
    IEmailDeliverySettingsWriter emailDeliverySettingsWriter,
    IUnitOfWork unitOfWork,
    IVisitorAccessSettingsWriter visitorSettings)
    : IRequestHandler<LockControlPlaneTenantSettingCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        LockControlPlaneTenantSettingCommand request,
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
                "The setting cannot be locked.");
        }

        if (!definition.IsLockable)
        {
            return ControlPlaneTenantSettingSecurity.Failure(
                request.TenantId,
                "setting_not_lockable",
                "The setting cannot be locked.");
        }

        (BaseCommandResponse<Guid> Response, SettingChangedNotification? Notification) outcome =
            EmailDeliverySettingKeys.Contains(request.Key) || VisitorAccessSettingMutationGuard.Handles(request.Key)
                ? await mutationLock.ExecuteOrderedGroupsAsync(
                    [VisitorAccessSettingMutationGuard.Handles(request.Key)
                        ? Explore.Application.Services.VisitorAccessCapabilityResolver.AuthoritySettingKeys : EmailDeliverySettingKeys.All],
                    token => unitOfWork.ExecuteSerializableAsync(ApplyAsync, token),
                    cancellationToken)
                : await mutationLock.ExecuteAsync(request.Key, ApplyAsync, cancellationToken);

        if (outcome.Notification is not null)
        {
            settingsResolver.InvalidateCache(SettingScope.Tenant, request.TenantId);
            await mediator.Publish(outcome.Notification, CancellationToken.None);
        }

        return outcome.Response;

        async Task<(BaseCommandResponse<Guid> Response, SettingChangedNotification? Notification)>
            ApplyAsync(CancellationToken token)
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

                if (existing.IsLocked)
                {
                    return (ControlPlaneTenantSettingSecurity.Failure(
                        request.TenantId, "setting_state_conflict", "The tenant setting is already locked."), null);
                }

                if (VisitorAccessSettingMutationGuard.Handles(request.Key))
                {
                    var result = await visitorSettings.ApplyAsync(
                        [new(request.TenantId, request.Key, VisitorAccessSettingMutationKind.SetLock, IsLocked: true)],
                        actorUserId, token);
                    return (result.ToCommandResponse(request.TenantId), result.DeferredNotifications.SingleOrDefault());
                }

                if (EmailDeliverySettingKeys.Contains(request.Key))
                {
                    EmailDeliverySettingsWriteResult result = await emailDeliverySettingsWriter.ApplyAsync(
                        [new EmailDeliverySettingMutation(
                            TenantId: request.TenantId,
                            Key: request.Key,
                            Kind: EmailDeliverySettingMutationKind.SetLock,
                            IsLocked: true)],
                        actorUserId,
                        token);
                    return (
                        result.ToCommandResponse(request.TenantId, "Tenant setting locked."),
                        result.IsAccepted() ? result.ToNotifications(actorUserId).SingleOrDefault() : null);
                }

                bool applied = await repository.LockAsync(
                    request.TenantId,
                    request.Key,
                    actorUserId.Value,
                    token);
                if (!applied)
                {
                    return (ControlPlaneTenantSettingSecurity.Failure(
                        request.TenantId, "setting_state_conflict", "The tenant setting state changed before it could be locked."), null);
                }

                BaseCommandResponse<Guid> response = BaseCommandResponse.Success(
                    request.TenantId,
                    "Tenant setting locked.");
                var notification = new SettingChangedNotification(
                    request.Key,
                    existing.Value,
                    existing.Value,
                    SettingSource.TenantLocked,
                    request.TenantId,
                    actorUserId,
                    DateTime.UtcNow);
                return (response, notification);
            }
    }
}
