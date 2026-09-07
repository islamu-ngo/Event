using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventSessionGroups.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using MediatR;

namespace Explore.Application.Features.EventSessionGroups.Handlers.Commands;

public class DeleteEventSessionGroupCommandHandler : IRequestHandler<DeleteEventSessionGroupCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionGroupRepository _eventSessionGroupRepository;
    private readonly IEventSessionGroupSessionRepository _eventSessionGroupSessionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly EventLocationAttachmentService _eventLocationAttachmentService;

    public DeleteEventSessionGroupCommandHandler(
        IEventSessionGroupRepository eventSessionGroupRepository,
        IEventSessionGroupSessionRepository eventSessionGroupSessionRepository,
        IUnitOfWork unitOfWork,
        EventLocationAttachmentService eventLocationAttachmentService)
    {
        _eventSessionGroupRepository = eventSessionGroupRepository;
        _eventSessionGroupSessionRepository = eventSessionGroupSessionRepository;
        _unitOfWork = unitOfWork;
        _eventLocationAttachmentService = eventLocationAttachmentService;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(DeleteEventSessionGroupCommand request, CancellationToken cancellationToken)
    {
        var group = await _eventSessionGroupRepository.GetForUpdateAsync(request.Id, cancellationToken);
        if (group is null)
        {
            return BaseCommandResponse.NotFound<Guid>("Event session group not found.");
        }

        if (group.EventId != request.EventId)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Event session group must belong to the requested event."],
                "Event session group must belong to the requested event.");
        }

        await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            Guid? eventLocationId = group.EventLocationId;
            var assignments = await _eventSessionGroupSessionRepository.GetAssignmentsForGroupUpdateAsync(
                group.Id,
                token);
            foreach (var assignment in assignments)
            {
                await _eventSessionGroupSessionRepository.Delete(assignment);
            }

            group.DetachEventLocationForDeletion();
            await _eventSessionGroupRepository.Delete(group);
            await _eventLocationAttachmentService.DetachIfUnreferencedAsync(
                eventLocationId,
                token);
        }, cancellationToken);

        return BaseCommandResponse.Success(group.Id, "Event session group deleted successfully.");
    }
}
