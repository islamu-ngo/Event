using Explore.Application.Mappings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Actor;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Actors.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Services;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Actors.Handlers.Queries;

public class GetActorListRequestHandler : IQueryHandler<GetActorListRequest, PaginatedResult<ActorListDto>>
{
    private readonly IActorRepository _actorRepository;
    private readonly IObjectStorageService _objectStorageService;
    private readonly ILogger<GetActorListRequestHandler> _logger;

    public GetActorListRequestHandler(
        IActorRepository actorRepository,
        IObjectStorageService objectStorageService,
        ILogger<GetActorListRequestHandler> logger)
    {
        _actorRepository = actorRepository;
        _objectStorageService = objectStorageService;
        _logger = logger;
    }

    public async Task<PaginatedResult<ActorListDto>> QueryAsync(GetActorListRequest request, CancellationToken cancellationToken = default)
    {
        var (pageNumber, pageSize) = PaginatedResult<ActorListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var (actors, totalCount) = await _actorRepository.GetActorsWithDetailsPaged(
            pageNumber,
            pageSize,
            cancellationToken);
        var dtos = actors.Select(ActorFederationMapper.ToActorListItem).ToList();

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
