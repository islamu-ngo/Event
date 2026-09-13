using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.EventCategories.Requests.Queries;
using Explore.Application.Services;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventCategories.Handlers.Queries;

public class GetEventsByCategoryRequestHandler : IQueryHandler<GetEventsByCategoryRequest, List<EventListDto>>
{
    private readonly IEventCategoriesRepository _eventCategoriesRepository;
    private readonly IObjectStorageService _objectStorageService;
    private readonly ILogger<GetEventsByCategoryRequestHandler> _logger;

    public GetEventsByCategoryRequestHandler(
        IEventCategoriesRepository eventCategoriesRepository,
        IObjectStorageService objectStorageService,
        ILogger<GetEventsByCategoryRequestHandler> logger)
    {
        _eventCategoriesRepository = eventCategoriesRepository;
        _objectStorageService = objectStorageService;
        _logger = logger;
    }

    public async Task<List<EventListDto>> QueryAsync(GetEventsByCategoryRequest request, CancellationToken cancellationToken)
    {
        var events = await _eventCategoriesRepository.GetEventsByCategory(request.CategoryId);
        var eventDtos = events.Select(EventMapper.ToListItem).ToList();

        // Resolve presigned URLs for images
        foreach (var dto in eventDtos)
        {
            dto.FeaturedImageUri = await ResolveImageUrl(dto.FeaturedImageUri);
            dto.ActorProfilePictureUri = await ResolveImageUrl(dto.ActorProfilePictureUri);
        }

        return eventDtos;
    }

    private Task<string?> ResolveImageUrl(string? objectKeyOrUri)
        => StoragePresentationUrlResolver.ResolveImageUrlAsync(
            objectKeyOrUri,
            _logger,
            "category event image");
}
