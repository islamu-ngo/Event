
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EmailDispatch.Handlers.Commands;

public sealed class DisableEmailDeliveryCommandHandler(
    IEmailDeliverySettingsWriter emailSettingsWriter,
    IEnumerable<Contracts.Operations.INotificationHandler<SettingChangedNotification>> notificationHandlers,
    IHumanCommitScopePreparation humanCommitScope) : ICommandHandler<DisableEmailDeliveryCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        DisableEmailDeliveryCommand request,
        CancellationToken cancellationToken)
    {
        await humanCommitScope.PrepareAsync(cancellationToken);
        Guid? actorId = humanCommitScope.Subject?.PrincipalId;
        if (actorId is null)
            return BaseCommandResponse.Authorization<Guid>(
                "Current administrator authority is required for the selected scope.");
        if (request.TenantId == Guid.Empty)
            return BaseCommandResponse.Validation<Guid>(["TenantId must not be empty."]);

        // The concrete Email writer prepares complete sources while idle, then owns its
        // single physical attempt. Do not pre-open a transaction around that preparation.
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
        foreach (var notification in result.IsAccepted() ? result.ToNotifications(actorId.Value) : [])
            await notificationHandlers.HandleAsync(notification, CancellationToken.None);
        return response;
    }
}
