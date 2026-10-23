using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventRoleAssignments.Handlers.Commands;

public sealed class AssignEventRoleByEmailCommandHandler(
    IUserRepository userRepository,
    IHumanEventTeamAssignmentOwner assignRoleHandler,
    IAdminContext identity,
    IMachinePrincipalAccessor machine,
    ITenantContext tenant,
    IAuthorizationProvider authorization,
    IHumanEventTeamActionPreparation preparation,
    ISerializableAttemptOwner owner)
    : EventRoleAssignmentCommandHandlerBase(identity, machine, tenant, authorization),
      ICommandHandler<AssignEventRoleByEmailCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        AssignEventRoleByEmailCommand command,
        CancellationToken cancellationToken = default)
    {
        await RequireActorAsync(command.TenantId, command.EventId, AuthorizationActions.Events.ManageTeam, cancellationToken);
        var prepared = await preparation.PrepareAsync(command, cancellationToken);
        return await owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(command, token);
            return await ExecuteBodyAsync(command, prepared, token);
        }, cancellationToken);
    }

    private async Task<BaseCommandResponse<Guid>> ExecuteBodyAsync(AssignEventRoleByEmailCommand command,
        IPreparedHumanEventTeamAction prepared, CancellationToken cancellationToken)
    {
        var targetUserEmail = command.TargetUserEmail.Trim();
        if (string.IsNullOrWhiteSpace(targetUserEmail))
            return Failure("Target user email is required.", "target_user_email_required");

        var targetUser = await userRepository.GetUserByEmail(targetUserEmail);
        if (targetUser is null)
            return Failure("Target user not found.", "target_user_not_found");

        var inner = prepared.BindResolvedTarget(command, targetUser.Id);
        return await assignRoleHandler.ExecuteHandoffAsync(inner, prepared, cancellationToken);
    }

}
