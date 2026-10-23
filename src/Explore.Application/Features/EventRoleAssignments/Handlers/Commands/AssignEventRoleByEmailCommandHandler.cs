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
    AssignEventRoleCommandHandler assignRoleHandler,
    IAdminContext identity,
    IMachinePrincipalAccessor machine,
    ITenantContext tenant,
    IAuthorizationProvider authorization)
    : EventRoleAssignmentCommandHandlerBase(identity, machine, tenant, authorization),
      ICommandHandler<AssignEventRoleByEmailCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        AssignEventRoleByEmailCommand command,
        CancellationToken cancellationToken = default)
    {
        await RequireActorAsync(command.TenantId, command.EventId, AuthorizationActions.Events.ManageTeam, cancellationToken);
        var targetUserEmail = command.TargetUserEmail.Trim();
        if (string.IsNullOrWhiteSpace(targetUserEmail))
            return Failure("Target user email is required.", "target_user_email_required");

        var targetUser = await userRepository.GetUserByEmail(targetUserEmail);
        if (targetUser is null)
            return Failure("Target user not found.", "target_user_not_found");

        return await assignRoleHandler.ExecuteQualifiedAsync(new AssignEventRoleCommand
        {
            TenantId = command.TenantId,
            EventId = command.EventId,
            TargetUserId = targetUser.Id,
            RoleId = command.RoleId,
            Status = command.Status,
            StartsAtUtc = command.StartsAtUtc,
            ExpiresAtUtc = command.ExpiresAtUtc
        }, targetUserEmail, cancellationToken);
    }

}
