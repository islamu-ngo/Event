using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Commands;
using Explore.Application.Services;

namespace Explore.Application.Features.EventSessionAgendaItems.Handlers.Commands;

public class DeleteEventSessionAgendaItemCommandHandler : ICommandHandler<DeleteEventSessionAgendaItemCommand, bool>
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

    public async Task<bool> ExecuteAsync(DeleteEventSessionAgendaItemCommand command, CancellationToken cancellationToken = default)
    {
        var agendaItem = await _agendaItemRepository.GetById(command.Id);

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
