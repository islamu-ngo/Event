using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Features.EventSessions.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessions.Handlers.Queries;

public sealed class GetManagedEventSessionDetailsRequestHandler(
    IEventSessionRepository eventSessionRepository,
    IMapper mapper)
    : IRequestHandler<GetManagedEventSessionDetailsRequest, EventSessionDto?>
{
    public async Task<EventSessionDto?> Handle(
        GetManagedEventSessionDetailsRequest request,
        CancellationToken cancellationToken)
    {
        var session = await eventSessionRepository.GetSessionWithDetails(request.Id);
        if (session?.EventId != request.EventId)
            return null;

        var dto = mapper.Map<EventSessionDto>(session);
        dto.LocationId = session.LocationId;
        dto.LocationFullName = session.Location?.FullName;
        dto.LocationAddress = session.Location?.Address;
        dto.LocationCity = session.Location?.City;
        dto.LocationCountry = session.Location?.Country;
        dto.RoomId = session.RoomId;
        dto.RoomName = session.Room?.Name;
        return dto;
    }
}
