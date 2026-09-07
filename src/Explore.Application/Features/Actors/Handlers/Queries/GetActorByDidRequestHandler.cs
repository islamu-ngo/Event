using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Actor;
using Explore.Application.Features.Actors.Requests.Queries;
using Explore.Application.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Actors.Handlers.Queries;

public class GetActorByDidRequestHandler : IRequestHandler<GetActorByDidRequest, ActorDto>
{
    private readonly IActorRepository _actorRepository;
    private readonly IMapper _mapper;
    private readonly IObjectStorageService _objectStorageService;
    private readonly ILogger<GetActorByDidRequestHandler> _logger;

    public GetActorByDidRequestHandler(
        IActorRepository actorRepository,
        IMapper mapper,
        IObjectStorageService objectStorageService,
        ILogger<GetActorByDidRequestHandler> logger)
    {
        _actorRepository = actorRepository;
        _mapper = mapper;
        _objectStorageService = objectStorageService;
        _logger = logger;
    }

    public async Task<ActorDto> Handle(GetActorByDidRequest request, CancellationToken cancellationToken)
    {
        var actor = await _actorRepository.GetActorByDid(request.Did, cancellationToken);
        var dto = _mapper.Map<ActorDto>(actor);

        // Resolve presigned URL for profile picture
        if (dto != null)
        {
            dto.ProfilePictureUri = await ResolveImageUrl(dto.ProfilePictureUri);
        }

        return dto;
    }

    private Task<string?> ResolveImageUrl(string? objectKeyOrUri)
        => StoragePresentationUrlResolver.ResolveImageUrlAsync(
            objectKeyOrUri,
            _logger,
            "actor DID profile image");
}
