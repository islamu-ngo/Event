using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Actor;
using Explore.Application.Features.Actors.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Actors.Handlers.Queries;

public class GetActorListRequestHandler : IRequestHandler<GetActorListRequest, PaginatedResult<ActorListDto>>
{
    private readonly IActorRepository _actorRepository;
    private readonly IMapper _mapper;
    private readonly IObjectStorageService _objectStorageService;
    private readonly ILogger<GetActorListRequestHandler> _logger;

    public GetActorListRequestHandler(
        IActorRepository actorRepository,
        IMapper mapper,
        IObjectStorageService objectStorageService,
        ILogger<GetActorListRequestHandler> logger)
    {
        _actorRepository = actorRepository;
        _mapper = mapper;
        _objectStorageService = objectStorageService;
        _logger = logger;
    }

    public async Task<PaginatedResult<ActorListDto>> Handle(GetActorListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<ActorListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var (actors, totalCount) = await _actorRepository.GetActorsWithDetailsPaged(
            pageNumber,
            pageSize,
            cancellationToken);
        var dtos = _mapper.Map<List<ActorListDto>>(actors);

        // Resolve presigned URLs for profile pictures
        foreach (var dto in dtos)
        {
            dto.ProfilePictureUri = await ResolveImageUrl(dto.ProfilePictureUri);
        }

        return PaginatedResult<ActorListDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }

    private Task<string?> ResolveImageUrl(string? objectKeyOrUri)
        => StoragePresentationUrlResolver.ResolveImageUrlAsync(
            objectKeyOrUri,
            _logger,
            "actor list profile image");
}
