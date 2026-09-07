using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventAgendaItems.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using MediatR;

namespace Explore.Application.Features.EventAgendaItems.Handlers.Commands;

public class DeleteEventAgendaItemCommandHandler : IRequestHandler<DeleteEventAgendaItemCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventAgendaItemRepository _eventAgendaItemRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly EventLocationAttachmentService _eventLocationAttachmentService;

    public DeleteEventAgendaItemCommandHandler(
        IEventAgendaItemRepository eventAgendaItemRepository,
        IUnitOfWork unitOfWork,
        EventLocationAttachmentService eventLocationAttachmentService)
    {
        _eventAgendaItemRepository = eventAgendaItemRepository;
        _unitOfWork = unitOfWork;
        _eventLocationAttachmentService = eventLocationAttachmentService;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(DeleteEventAgendaItemCommand request, CancellationToken cancellationToken)
    {
        var agendaItem = await _eventAgendaItemRepository.GetById(request.Id);
        if (agendaItem == null)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Event agenda item not found."],
                "Event agenda item not found.");
        }

        await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            Guid? eventLocationId = agendaItem.EventLocationId;
            agendaItem.DetachEventLocationForDeletion();
            await _eventAgendaItemRepository.Delete(agendaItem);
            await _eventLocationAttachmentService.DetachIfUnreferencedAsync(
                eventLocationId,
                token);
        }, cancellationToken);

        return BaseCommandResponse.Success(agendaItem.Id, "Event agenda item deleted successfully.");
    }
}
