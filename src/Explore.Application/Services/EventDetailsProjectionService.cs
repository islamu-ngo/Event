using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Category;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.Tag;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Services;

public sealed class EventDetailsProjectionService : IEventDetailsProjectionService
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventModerationRecordRepository _eventModerationRecordRepository;
    private readonly IEventTagsRepository _eventTagsRepository;
    private readonly IEventCategoriesRepository _eventCategoriesRepository;
    private readonly IMapper _mapper;
    private readonly IObjectStorageService _objectStorageService;
    private readonly ILogger<EventDetailsProjectionService> _logger;

    public EventDetailsProjectionService(
        IEventRepository eventRepository,
        IEventModerationRecordRepository eventModerationRecordRepository,
        IEventTagsRepository eventTagsRepository,
        IEventCategoriesRepository eventCategoriesRepository,
        IMapper mapper,
        IObjectStorageService objectStorageService,
        ILogger<EventDetailsProjectionService> logger)
    {
        _eventRepository = eventRepository;
        _eventModerationRecordRepository = eventModerationRecordRepository;
        _eventTagsRepository = eventTagsRepository;
        _eventCategoriesRepository = eventCategoriesRepository;
        _mapper = mapper;
        _objectStorageService = objectStorageService;
        _logger = logger;
    }

    public async Task<EventDto?> BuildAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var @event = await _eventRepository.GetEventWithDetails(eventId);
        return await BuildAsync(@event, cancellationToken);
    }

    public async Task<EventDto?> BuildByPublicCodeAsync(string publicCode, CancellationToken cancellationToken)
    {
        var @event = await _eventRepository.GetPublicEventWithDetailsByCodeAsync(publicCode, cancellationToken);
        return await BuildAsync(@event, cancellationToken);
    }

    private async Task<EventDto?> BuildAsync(Explore.Domain.Event? @event, CancellationToken cancellationToken)
    {
        var dto = _mapper.Map<EventDto>(@event);

        if (dto is null)
            return null;

        var eventId = dto.Id;

        var latestModerationRecord = await _eventModerationRecordRepository.GetLatestByEventAsync(
            dto.TenantId,
            eventId,
            cancellationToken);
        var tags = await _eventTagsRepository.GetTagsByEvent(eventId);
        var categories = await _eventCategoriesRepository.GetCategoriesByEvent(eventId);

        dto.IsUnmoderationEligible = latestModerationRecord?.AllowsUnmoderation == true;
        return dto with
        {
            Tags = _mapper.Map<List<TagListDto>>(tags),
            Categories = _mapper.Map<List<CategoryListDto>>(categories)
        };
    }

    public async Task ResolveImageUrlsAsync(EventDto eventDto, CancellationToken cancellationToken)
    {
        eventDto.FeaturedImageUri = await ResolveImageUrlAsync(eventDto.FeaturedImageUri);
        eventDto.ActorProfilePictureUri = await ResolveImageUrlAsync(eventDto.ActorProfilePictureUri);
    }

    private Task<string?> ResolveImageUrlAsync(string? objectKeyOrUri)
        => StoragePresentationUrlResolver.ResolveImageUrlAsync(
            objectKeyOrUri,
            _logger,
            "event detail image");
}
