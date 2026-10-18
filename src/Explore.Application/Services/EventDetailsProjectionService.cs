using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Mappings;
using Explore.Application.DTOs.Event;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Services;

public sealed class EventDetailsProjectionService : IEventDetailsProjectionService
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventModerationRecordRepository _eventModerationRecordRepository;
    private readonly IEventTagsRepository _eventTagsRepository;
    private readonly IEventCategoriesRepository _eventCategoriesRepository;
    private readonly IObjectStorageService _objectStorageService;
    private readonly ILogger<EventDetailsProjectionService> _logger;
    private readonly IPrivateEventManagementReader _privateManagement;

    public EventDetailsProjectionService(
        IEventRepository eventRepository,
        IEventModerationRecordRepository eventModerationRecordRepository,
        IEventTagsRepository eventTagsRepository,
        IEventCategoriesRepository eventCategoriesRepository,
        IObjectStorageService objectStorageService,
        ILogger<EventDetailsProjectionService> logger,
        IPrivateEventManagementReader privateManagement)
    {
        _eventRepository = eventRepository;
        _eventModerationRecordRepository = eventModerationRecordRepository;
        _eventTagsRepository = eventTagsRepository;
        _eventCategoriesRepository = eventCategoriesRepository;
        _objectStorageService = objectStorageService;
        _logger = logger;
        _privateManagement = privateManagement;
    }

    public async Task<EventDto?> BuildAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var graph = await _privateManagement.ReadAsync(new(PrivateEventManagementReadKind.Details, eventId), cancellationToken);
        var dto = EventMapper.ToDetail(graph.Events.SingleOrDefault());
        if (dto is null) return null;
        dto.IsUnmoderationEligible = graph.ModerationRecords.OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.Id).FirstOrDefault()?.AllowsUnmoderation == true;
        return dto with
        {
            Tags = graph.Tags.Select(TagMapper.ToListItem).ToList(),
            Categories = graph.Categories.Select(CustomPropertyMapper.ToListItem).ToList()
        };
    }

    public async Task<EventDto?> BuildByPublicCodeAsync(string publicCode, CancellationToken cancellationToken)
    {
        var @event = await _eventRepository.GetPublicEventWithDetailsByCodeAsync(publicCode, cancellationToken);
        return await BuildAsync(@event, cancellationToken);
    }

    public async Task<EventDto?> BuildPublicAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var @event = await _eventRepository.GetPublicEventWithDetailsByIdAsync(eventId, cancellationToken);
        return await BuildAsync(@event, cancellationToken);
    }

    private async Task<EventDto?> BuildAsync(Explore.Domain.Event? @event, CancellationToken cancellationToken)
    {
        var dto = EventMapper.ToDetail(@event);

        if (dto is null)
            return null;

        if (@event!.DiscoveryPresentation is { } presentation)
        {
            dto.IsUnmoderationEligible = presentation.IsUnmoderationEligible;
            return dto with
            {
                Tags = presentation.Tags.Select(tag => new Explore.Application.DTOs.Tag.TagListDto
                {
                    Id = tag.Id,
                    MasterCode = tag.MasterCode,
                    FullName = tag.FullName
                }).ToList(),
                Categories = presentation.Categories.Select(category => new Explore.Application.DTOs.Category.CategoryListDto
                {
                    Id = category.Id,
                    ConcurrencyStamp = category.ConcurrencyStamp,
                    MasterCode = category.MasterCode,
                    FullName = category.FullName,
                    ParentId = category.ParentId
                }).ToList()
            };
        }

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
            Tags = tags.Select(TagMapper.ToListItem).ToList(),
            Categories = categories.Select(CustomPropertyMapper.ToListItem).ToList()
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
