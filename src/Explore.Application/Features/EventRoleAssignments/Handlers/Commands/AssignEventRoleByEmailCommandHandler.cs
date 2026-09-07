using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventRoleAssignments.Handlers.Commands;

public sealed class AssignEventRoleByEmailCommandHandler(
    IUserRepository userRepository,
    IMediator mediator) : IRequestHandler<AssignEventRoleByEmailCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        AssignEventRoleByEmailCommand request,
        CancellationToken cancellationToken)
    {
        var targetUserEmail = request.TargetUserEmail.Trim();
        if (string.IsNullOrWhiteSpace(targetUserEmail))
            return Failure("Target user email is required.", "target_user_email_required");

        var targetUser = await userRepository.GetUserByEmail(targetUserEmail);
        if (targetUser is null)
            return Failure("Target user not found.", "target_user_not_found");

        return await mediator.Send(new AssignEventRoleCommand
        {
            TenantId = request.TenantId,
            EventId = request.EventId,
            TargetUserId = targetUser.Id,
            RoleId = request.RoleId,
            ActorUserId = request.ActorUserId,
            Status = request.Status,
            StartsAtUtc = request.StartsAtUtc,
            ExpiresAtUtc = request.ExpiresAtUtc
        }, cancellationToken);
    }

    private static BaseCommandResponse<Guid> Failure(string message, string failureCode) =>
        BaseCommandResponse.Failure<Guid>(failureCode, message);
}
