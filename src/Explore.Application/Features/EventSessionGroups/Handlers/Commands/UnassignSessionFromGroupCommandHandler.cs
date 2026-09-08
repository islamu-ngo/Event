using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventSessionGroups.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionGroups.Handlers.Commands;

public class UnassignSessionFromGroupCommandHandler : IRequestHandler<UnassignSessionFromGroupCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionGroupSessionRepository _eventSessionGroupSessionRepository;

    public UnassignSessionFromGroupCommandHandler(IEventSessionGroupSessionRepository eventSessionGroupSessionRepository)
    {
        _eventSessionGroupSessionRepository = eventSessionGroupSessionRepository;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UnassignSessionFromGroupCommand request, CancellationToken cancellationToken)
    {
        var assignment = await _eventSessionGroupSessionRepository.GetExistingAssignmentAsync(
            request.EventSessionGroupId,
            request.EventSessionId,
            cancellationToken);

        if (assignment is null)
        {
            return BaseCommandResponse.NotFound<Guid>("Session group assignment not found.");
        }

        if (assignment.EventId != request.EventId)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Session group assignment must belong to the requested event."],
                "Session group assignment must belong to the requested event.");
        }

        await _eventSessionGroupSessionRepository.Delete(assignment);

        return BaseCommandResponse.Success(assignment.Id, "Session group assignment removed successfully.");
    }
}
