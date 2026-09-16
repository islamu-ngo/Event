using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventRoleAssignments.Handlers.Commands;

public sealed class AssignEventRoleByEmailCommandHandler(
    IUserRepository userRepository,
    ICommandHandler<AssignEventRoleCommand, BaseCommandResponse<Guid>> assignRoleHandler)
    : ICommandHandler<AssignEventRoleByEmailCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        AssignEventRoleByEmailCommand command,
        CancellationToken cancellationToken = default)
    {
        var targetUserEmail = command.TargetUserEmail.Trim();
        if (string.IsNullOrWhiteSpace(targetUserEmail))
            return Failure("Target user email is required.", "target_user_email_required");

        var targetUser = await userRepository.GetUserByEmail(targetUserEmail);
        if (targetUser is null)
            return Failure("Target user not found.", "target_user_not_found");

        return await assignRoleHandler.ExecuteAsync(new AssignEventRoleCommand
        {
            TenantId = command.TenantId,
            EventId = command.EventId,
            TargetUserId = targetUser.Id,
            RoleId = command.RoleId,
            ActorUserId = command.ActorUserId,
            Status = command.Status,
            StartsAtUtc = command.StartsAtUtc,
            ExpiresAtUtc = command.ExpiresAtUtc
        }, cancellationToken);
    }

    private static BaseCommandResponse<Guid> Failure(string message, string failureCode) =>
        BaseCommandResponse.Failure<Guid>(failureCode, message);
}
