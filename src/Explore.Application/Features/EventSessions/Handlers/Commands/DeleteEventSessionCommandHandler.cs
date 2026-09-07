using System;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventSessions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventSessions.Handlers.Commands;

public class DeleteEventSessionCommandHandler : IRequestHandler<DeleteEventSessionCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IEventTicketCatalogRepository _catalogs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly EventLocationAttachmentService _eventLocationAttachmentService;

    public DeleteEventSessionCommandHandler(
        IEventSessionRepository eventSessionRepository,
        IEventTicketCatalogRepository catalogs,
        IUnitOfWork unitOfWork,
        EventLocationAttachmentService eventLocationAttachmentService)
    {
        _eventSessionRepository = eventSessionRepository;
        _catalogs = catalogs;
        _unitOfWork = unitOfWork;
        _eventLocationAttachmentService = eventLocationAttachmentService;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(DeleteEventSessionCommand request, CancellationToken cancellationToken)
    {
        var eventSession = await _eventSessionRepository.GetById(request.Id);

        if (eventSession == null)
        {
            return NotFound(request.Id);
        }

        DeleteResult result = await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await _catalogs.GetDraftCatalogForUpdateAsync(eventSession.EventId, eventSession.TenantId, token);
            EventTicketCatalogVersion? published = await _catalogs.GetPublishedForUpdateAsync(
                eventSession.EventId,
                eventSession.TenantId,
                token);
            EventSession? lockedSession = await _eventSessionRepository.GetByIdForEventForUpdateAsync(
                eventSession.Id,
                eventSession.EventId,
                eventSession.TenantId,
                token);
            if (lockedSession is null)
            {
                return DeleteResult.NotFound;
            }

            if (ReferencesEventSession(published, lockedSession.Id))
            {
                return DeleteResult.Referenced;
            }

            Guid? eventLocationId = lockedSession.EventLocationId;
            lockedSession.DetachEventLocationForDeletion();
            await _eventSessionRepository.Delete(lockedSession);
            await _eventLocationAttachmentService.DetachIfUnreferencedAsync(
                eventLocationId,
                token);
            return DeleteResult.Deleted;
        }, cancellationToken);

        return result switch
        {
            DeleteResult.Deleted => Success(eventSession.Id),
            DeleteResult.Referenced => Conflict(eventSession.Id),
            _ => NotFound(eventSession.Id)
        };
    }

    private static bool ReferencesEventSession(EventTicketCatalogVersion? catalog, Guid eventSessionId) =>
        catalog?.TicketTypes
            .Where(ticketType => !ticketType.IsDeleted)
            .SelectMany(ticketType => ticketType.Entitlements)
            .Any(entitlement => entitlement.EventSessionId == eventSessionId) == true;

    private static BaseCommandResponse<Guid> Success(Guid id) =>
        BaseCommandResponse.Success(id, "Event session deleted successfully.");

    private static BaseCommandResponse<Guid> NotFound(Guid id) =>
        BaseCommandResponse.Failure<Guid>(
            "event_session_not_found",
            "Event session not found.",
            id: id);

    private static BaseCommandResponse<Guid> Conflict(Guid id) =>
        BaseCommandResponse.Failure<Guid>(
            "event_session_ticket_entitlement_conflict",
            "Event session is referenced by a published ticket catalog.",
            id: id);

    private enum DeleteResult
    {
        Deleted,
        NotFound,
        Referenced
    }
}
