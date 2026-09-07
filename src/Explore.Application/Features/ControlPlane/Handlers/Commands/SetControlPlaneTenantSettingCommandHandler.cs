using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Features.Settings.Handlers;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Handlers.Commands;

public sealed class SetControlPlaneTenantSettingCommandHandler(
    ITenantSettingRepository repository,
    ISystemSettingRepository systemSettingRepository,
    ISettingMutationLock mutationLock,
    ICurrentUserService currentUserService,
    IHierarchicalSettingsResolver settingsResolver,
    IMediator mediator,
    IPublicationPolicyMutationBoundary publicationPolicyMutationBoundary,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SetControlPlaneTenantSettingCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        SetControlPlaneTenantSettingCommand request,
        CancellationToken cancellationToken)
    {
        Guid? currentActorUserId = currentUserService.UserId;
        if (currentActorUserId is null || currentActorUserId == Guid.Empty)
        {
            return ControlPlaneTenantSettingSecurity.Failure(
                request.TenantId,
                "authenticated_operator_required",
                "Authenticated operator context is required.");
        }

        Guid actorUserId = currentActorUserId.Value;
        BaseCommandResponse<Guid>? invalidTarget = ControlPlaneTenantSettingSecurity.ValidateTarget(
            request.TenantId,
            request.Key,
            out SettingDefinition definition);
        if (invalidTarget is not null)
        {
            return invalidTarget;
        }

        (bool isValid, string? serializedValue, _) =
            SettingCommandHelper.ValidateAndSerialize(request.Value, definition);
        if (!isValid)
        {
            return ControlPlaneTenantSettingSecurity.Failure(
                request.TenantId,
                "setting_validation_failed",
                "The tenant setting value is invalid.");
        }

        if (PublicationPolicySettingKeys.All.Contains(request.Key, StringComparer.Ordinal))
        {
            (BaseCommandResponse<Guid> Response, IReadOnlyList<SettingChangedNotification> Notifications) outcome =
                await unitOfWork.ExecuteInTransactionAsync(
                    async token =>
                    {
                        DateTime occurredAtUtc = DateTime.UtcNow;
                        PublicationPolicyMutationResult boundaryResult =
                            await publicationPolicyMutationBoundary.ApplyTenantAsync(
                                new PublicationPolicyTenantMutationRequest(
                                    request.TenantId,
                                    actorUserId,
                                    occurredAtUtc,
                                    [new PublicationPolicySettingMutation(
                                        request.Key,
                                        PublicationPolicyMutationKind.Set,
                                        serializedValue,
                                        request.TenantId,
                                        IsLocked: null)],
                                    PublicationPolicyLockedSystemBehavior.Reject),
                                token);
                        if (!boundaryResult.Success)
                        {
                            string failureCode = string.IsNullOrWhiteSpace(boundaryResult.FailureCode)
                                ? "event_reporting_intake_policy_invalid"
                                : boundaryResult.FailureCode;
                            return (
                                ControlPlaneTenantSettingSecurity.Failure(
                                    request.TenantId,
                                    failureCode,
                                    boundaryResult.Message),
                                (IReadOnlyList<SettingChangedNotification>)[]);
                        }

                        return (
                            BaseCommandResponse.Success(
                                request.TenantId,
                                "Tenant setting updated."),
                            (IReadOnlyList<SettingChangedNotification>)boundaryResult.DeferredNotifications);
                    },
                    cancellationToken);

            if (outcome.Notifications.Count > 0)
            {
                settingsResolver.InvalidateCache(SettingScope.Tenant, request.TenantId);
                foreach (SettingChangedNotification notification in outcome.Notifications)
                {
                    await mediator.Publish(notification, cancellationToken);
                }
            }

            return outcome.Response;
        }

        (BaseCommandResponse<Guid> Response, SettingChangedNotification? Notification) unguardedOutcome =
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
                    request.TenantId, request.Key, token);
                await repository.SetValueAsync(
                    request.TenantId,
                    request.Key,
                    serializedValue!,
                    token,
                    actorUserId);
                BaseCommandResponse<Guid> response = BaseCommandResponse.Success(
                    request.TenantId,
                    "Tenant setting updated.");
                var notification = new SettingChangedNotification(
                    request.Key,
                    existing?.Value,
                    serializedValue,
                    SettingSource.TenantOverride,
                    request.TenantId,
                    actorUserId,
                    DateTime.UtcNow);
                return (response, notification);
            },
            cancellationToken);

        if (unguardedOutcome.Notification is not null)
        {
            settingsResolver.InvalidateCache(SettingScope.Tenant, request.TenantId);
            await mediator.Publish(unguardedOutcome.Notification, cancellationToken);
        }

        return unguardedOutcome.Response;
    }
}
