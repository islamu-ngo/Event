using AutoMapper;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Events.Handlers.Queries;

public class GetManagedEventsByActorRequestHandler : IRequestHandler<GetManagedEventsByActorRequest, PaginatedResult<EventListDto>>
{
    private const int MaxPageSize = 100;

    private readonly IEventRepository _eventRepository;
    private readonly IAuthorizationProvider _authorizationProvider;
    private readonly IMapper _mapper;
    private readonly IObjectStorageService _objectStorageService;
    private readonly ILogger<GetManagedEventsByActorRequestHandler> _logger;

    public GetManagedEventsByActorRequestHandler(
        IEventRepository eventRepository,
        IAuthorizationProvider authorizationProvider,
        IMapper mapper,
        IObjectStorageService objectStorageService,
        ILogger<GetManagedEventsByActorRequestHandler> logger)
    {
        _eventRepository = eventRepository;
        _authorizationProvider = authorizationProvider;
        _mapper = mapper;
        _objectStorageService = objectStorageService;
        _logger = logger;
    }

    public async Task<PaginatedResult<EventListDto>> Handle(GetManagedEventsByActorRequest request, CancellationToken cancellationToken)
    {
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, MaxPageSize);

        if (request.ActorId == Guid.Empty)
        {
            return PaginatedResult<EventListDto>.Create([], 0, pageNumber, pageSize);
        }

        var events = await _eventRepository.GetEventsByActorWithDetails(request.ActorId, cancellationToken);
        var eventDtos = _mapper.Map<List<EventListDto>>(events);
        var authorizedEvents = await FilterViewManagementAuthorizedAsync(eventDtos, cancellationToken);

        var pageItems = authorizedEvents
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        foreach (var dto in pageItems)
        {
            dto.IsManagementView = true;
            dto.FeaturedImageUri = await ResolveImageUrl(dto.FeaturedImageUri);
            dto.ActorProfilePictureUri = await ResolveImageUrl(dto.ActorProfilePictureUri);
        }

        return PaginatedResult<EventListDto>.Create(pageItems, authorizedEvents.Count, pageNumber, pageSize);
    }

    private async Task<List<EventListDto>> FilterViewManagementAuthorizedAsync(
        IReadOnlyList<EventListDto> eventDtos,
        CancellationToken cancellationToken)
    {
        if (eventDtos.Count == 0)
            return [];

        var descriptor = ResourceDescriptors.EventList;
        var checks = eventDtos
            .Select(dto => new AuthorizationRequest(
                descriptor.Kind,
                descriptor.GetResourceId(dto),
                AuthorizationActions.Events.ViewManagement,
                Scope: descriptor.GetScope(dto),
                Facts: descriptor.GetFacts(dto)))
            .ToList();

        IReadOnlyList<bool> decisions;
        try
        {
            decisions = (await _authorizationProvider.AuthorizeBatchAsync(checks, cancellationToken))
                .Select(decision => decision.IsAllowed)
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to authorize managed event list for actor profile; denying all management-only events.");
            return [];
        }

        var authorized = new List<EventListDto>(eventDtos.Count);
        for (var i = 0; i < eventDtos.Count; i++)
        {
            if (i < decisions.Count && decisions[i])
            {
                authorized.Add(eventDtos[i]);
            }
        }

        return authorized;
    }

    private Task<string?> ResolveImageUrl(string? objectKeyOrUri)
        => StoragePresentationUrlResolver.ResolveImageUrlAsync(
            objectKeyOrUri,
            _logger,
            "managed actor event list image");
}
