using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventDays.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventDays.Handlers.Commands;

public class DeleteEventDayCommandHandler : IRequestHandler<DeleteEventDayCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventDayRepository _eventDayRepository;
    private readonly IEventTicketCatalogRepository _catalogs;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteEventDayCommandHandler(
        IEventDayRepository eventDayRepository,
        IEventTicketCatalogRepository catalogs,
        IUnitOfWork unitOfWork)
    {
        _eventDayRepository = eventDayRepository;
        _catalogs = catalogs;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(DeleteEventDayCommand request, CancellationToken cancellationToken)
    {
        var eventDay = await _eventDayRepository.GetById(request.Id);
        if (eventDay == null)
        {
            return BaseCommandResponse.Failure<Guid>(
                "event_day_not_found",
                "Event day not found.");
        }

        DeleteResult deleteResult = await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await _catalogs.GetDraftCatalogForUpdateAsync(eventDay.EventId, eventDay.TenantId, token);
            EventTicketCatalogVersion? published = await _catalogs.GetPublishedForUpdateAsync(
                eventDay.EventId,
                eventDay.TenantId,
                token);
            EventDay? lockedDay = await _eventDayRepository.GetByIdForEventForUpdateAsync(
                eventDay.Id,
                eventDay.EventId,
                eventDay.TenantId,
                token);
            if (lockedDay is null)
            {
                return DeleteResult.NotFound;
            }

            if (ReferencesEventDay(published, lockedDay.Id))
            {
                return DeleteResult.Referenced;
            }

            await _eventDayRepository.Delete(lockedDay);
            return DeleteResult.Deleted;
        }, cancellationToken);

        if (deleteResult != DeleteResult.Deleted)
        {
            string failureCode = deleteResult == DeleteResult.Referenced
                ? "event_day_ticket_entitlement_conflict"
                : "event_day_not_found";
            string message = deleteResult == DeleteResult.Referenced
                ? "Event day is referenced by a published ticket catalog."
                : "Event day not found.";
            return BaseCommandResponse.Failure<Guid>(failureCode, message);
        }

        return BaseCommandResponse.Success(eventDay.Id, "Event day deleted successfully.");
    }

    private static bool ReferencesEventDay(EventTicketCatalogVersion? catalog, Guid eventDayId) =>
        catalog?.TicketTypes
            .Where(ticketType => !ticketType.IsDeleted)
            .SelectMany(ticketType => ticketType.Entitlements)
            .Any(entitlement => entitlement.EventDayId == eventDayId) == true;

    private enum DeleteResult
    {
        Deleted,
        NotFound,
        Referenced
    }
}
