
using System.Collections.Immutable;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;
using MediatR;

namespace Explore.Application.Features.EmailDispatch.Handlers.Commands;

public sealed class DisableEmailDeliveryCommandHandler(
    IAdminContext adminContext,
    ITenantContext tenantContext,
    IEmailDeliverySettingsWriter emailSettingsWriter,
    ISettingMutationLock mutationLock,
    IUnitOfWork unitOfWork,
    IPublisher publisher,
    IPlatformUserRoleRepository platformRoles,
    ITenantUserRoleGrantRepository tenantRoles) : IRequestHandler<DisableEmailDeliveryCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        DisableEmailDeliveryCommand request,
        CancellationToken cancellationToken)
    {
        if (await ResolveAuthorizedActorAsync(request.TenantId, cancellationToken) is null)
            return BaseCommandResponse.Authorization<Guid>(
                "Current administrator authority is required for the selected scope.");
        if (request.TenantId == Guid.Empty)
            return BaseCommandResponse.Validation<Guid>(["TenantId must not be empty."]);

        var outcome = await mutationLock.ExecuteOrderedGroupsAsync(
            [EmailDeliverySettingKeys.All],
            token => unitOfWork.ExecuteSerializableAsync(
                transactionToken => DisableAsync(request, transactionToken), token), cancellationToken);

        foreach (var notification in outcome.Notifications)
            await publisher.Publish(notification, CancellationToken.None);
        return outcome.Response;
    }

    private async Task<(BaseCommandResponse<Guid> Response, ImmutableArray<SettingChangedNotification> Notifications)> DisableAsync(
        DisableEmailDeliveryCommand request,
        CancellationToken cancellationToken)
    {
        Guid? actorId = await ResolveAuthorizedActorAsync(request.TenantId, cancellationToken);
        if (actorId is null)
        {
            return (BaseCommandResponse.Authorization<Guid>(
                "Current administrator authority is required for the selected scope."), []);
        }

        var result = await emailSettingsWriter.DisableAsync(new EmailDeliveryDisableConfirmation(
            TenantId: request.TenantId,
            ActorUserId: actorId.Value,
            ExpectedRevision: request.ExpectedRevision,
            ConfirmationToken: request.ConfirmationToken,
            Acknowledgement: request.Acknowledgement), cancellationToken);
        var response = result.Status == EmailDeliverySettingsWriteStatus.Locked
            ? BaseCommandResponse.Conflict(request.TenantId ?? Guid.Empty,
                "Email delivery disable confirmation is no longer valid. Request a new preview.")
            : result.ToCommandResponse(request.TenantId ?? Guid.Empty, "Email delivery disabled.");
        return (response,
            result.IsAccepted() ? result.ToNotifications(actorId) : []);
    }

    private async Task<Guid?> ResolveAuthorizedActorAsync(Guid? targetTenantId, CancellationToken cancellationToken)
    {
        Guid? actorId = await adminContext.ResolveUserIdAsync(cancellationToken);
        if (actorId is null || actorId == Guid.Empty)
            return null;
        return await platformRoles.IsUserPlatformAdmin(actorId.Value)
            || (targetTenantId is Guid tenantId && tenantId != Guid.Empty
                && tenantContext.TenantId == tenantId
                && await tenantRoles.IsTenantAdminInCurrentTenantAsync(tenantId, actorId.Value, cancellationToken))
            ? actorId : null;
    }
}
