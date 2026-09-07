using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.EventTags.Requests.Queries;
using Explore.Application.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventTags.Handlers.Queries;

public class GetEventsByTagRequestHandler : IRequestHandler<GetEventsByTagRequest, List<EventListDto>>
{
    private readonly IEventTagsRepository _eventTagsRepository;
    private readonly IMapper _mapper;
    private readonly IObjectStorageService _objectStorageService;
    private readonly ILogger<GetEventsByTagRequestHandler> _logger;

    public GetEventsByTagRequestHandler(
        IEventTagsRepository eventTagsRepository,
        IMapper mapper,
        IObjectStorageService objectStorageService,
        ILogger<GetEventsByTagRequestHandler> logger)
    {
        _eventTagsRepository = eventTagsRepository;
        _mapper = mapper;
        _objectStorageService = objectStorageService;
        _logger = logger;
    }

    public async Task<List<EventListDto>> Handle(GetEventsByTagRequest request, CancellationToken cancellationToken)
    {
        var events = await _eventTagsRepository.GetEventsByTag(request.TagId);
        var eventDtos = _mapper.Map<List<EventListDto>>(events);

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
            "tag event image");
}
