using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.EventSessions.Requests.Queries;
using Explore.Application.Services;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventSessions.Handlers.Queries;

public class GetEventSessionDetailsRequestHandler : IRequestHandler<GetEventSessionDetailsRequest, EventSessionDto?>
{
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IMapper _mapper;
    private readonly IEventLocationDisclosureService _disclosureService;

    public GetEventSessionDetailsRequestHandler(
        IEventSessionRepository eventSessionRepository,
        IMapper mapper,
        IEventLocationDisclosureService disclosureService)
    {
        _eventSessionRepository = eventSessionRepository;
        _mapper = mapper;
        _disclosureService = disclosureService;
    }

    public async Task<EventSessionDto?> Handle(GetEventSessionDetailsRequest request, CancellationToken cancellationToken)
    {
        var eventSession = await _eventSessionRepository.GetPublicSessionWithDetailsAsync(
            request.Id,
            cancellationToken);
        return await PublicEventSessionLocationProjector.ProjectAsync(
            eventSession,
            _mapper,
            _disclosureService,
            cancellationToken);
    }
}

internal static class PublicEventSessionLocationProjector
{
    public static async Task<EventSessionDto?> ProjectAsync(
        EventSession? session,
        IMapper mapper,
        IEventLocationDisclosureService disclosureService,
        CancellationToken cancellationToken)
    {
        if (session is null)
        {
            return null;
        }

        IReadOnlyDictionary<Guid, EventLocationPublicDto> locations =
            await PublicEventLocationProjection.ResolveAsync(
                disclosureService,
                [Placement(session)],
                cancellationToken);
        EventSessionDto dto = mapper.Map<EventSessionDto>(session);
        ClearLegacyLocation(dto);
        dto.EventLocation = session.EventLocationId is { } eventLocationId
            ? locations.GetValueOrDefault(eventLocationId)
            : null;
        return dto;
    }

    public static async Task<List<EventSessionListDto>> ProjectAsync(
        IReadOnlyCollection<EventSession> sessions,
        IMapper mapper,
        IEventLocationDisclosureService disclosureService,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, EventLocationPublicDto> locations =
            await PublicEventLocationProjection.ResolveAsync(
                disclosureService,
                sessions.Select(Placement),
                cancellationToken);
        List<EventSessionListDto> dtos = mapper.Map<List<EventSessionListDto>>(sessions);
        IReadOnlyDictionary<Guid, EventSession> sessionById = sessions.ToDictionary(session => session.Id);
        foreach (EventSessionListDto dto in dtos)
        {
            ClearLegacyLocation(dto);
            EventSession session = sessionById[dto.Id];
            dto.EventLocation = session.EventLocationId is { } eventLocationId
                ? locations.GetValueOrDefault(eventLocationId)
                : null;
        }

        return dtos;
    }

    private static PublicEventLocationPlacement Placement(EventSession session)
        => new(
            session.TenantId,
            session.EventId,
            session.EventLocationId,
            session.RoomId);

    private static void ClearLegacyLocation(EventSessionDto dto)
    {
        dto.LocationId = null;
        dto.LocationFullName = null;
        dto.LocationAddress = null;
        dto.LocationCity = null;
        dto.LocationCountry = null;
        dto.RoomId = null;
        dto.RoomName = null;
    }

    private static void ClearLegacyLocation(EventSessionListDto dto)
    {
        dto.LocationId = null;
        dto.LocationFullName = null;
        dto.LocationCity = null;
        dto.RoomId = null;
        dto.RoomName = null;
    }
}
