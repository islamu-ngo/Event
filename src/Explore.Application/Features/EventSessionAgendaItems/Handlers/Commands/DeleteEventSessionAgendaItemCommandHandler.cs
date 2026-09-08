using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Commands;
using Explore.Application.Services;
using MediatR;

namespace Explore.Application.Features.EventSessionAgendaItems.Handlers.Commands;

public class DeleteEventSessionAgendaItemCommandHandler : IRequestHandler<DeleteEventSessionAgendaItemCommand, bool>
{
    private readonly IEventSessionAgendaItemRepository _agendaItemRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly EventLocationAttachmentService _eventLocationAttachmentService;

    public DeleteEventSessionAgendaItemCommandHandler(
        IEventSessionAgendaItemRepository agendaItemRepository,
        IUnitOfWork unitOfWork,
        EventLocationAttachmentService eventLocationAttachmentService)
    {
        _agendaItemRepository = agendaItemRepository;
        _unitOfWork = unitOfWork;
        _eventLocationAttachmentService = eventLocationAttachmentService;
    }

    public async Task<bool> Handle(DeleteEventSessionAgendaItemCommand request, CancellationToken cancellationToken)
    {
        var agendaItem = await _agendaItemRepository.GetById(request.Id);

        if (agendaItem == null)
        {
            return false;
        }

        await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await _agendaItemRepository.Delete(agendaItem);
            await _eventLocationAttachmentService.DetachIfUnreferencedAsync(
                agendaItem.EventLocationId,
                token);
        }, cancellationToken);

        return true;
    }
}
