using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Actor;
using Explore.Application.Features.Actors.Requests.Queries;
using Explore.Application.Services;
using Explore.Domain;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Actors.Handlers.Queries;

public class GetActorsByTenantRequestHandler : IRequestHandler<GetActorsByTenantRequest, List<ActorListDto>>
{
    private readonly IActorRepository _actorRepository;
    private readonly IMapper _mapper;
    private readonly ILogger<GetActorsByTenantRequestHandler> _logger;

    public GetActorsByTenantRequestHandler(
        IActorRepository actorRepository,
        IMapper mapper,
        ILogger<GetActorsByTenantRequestHandler> logger)
    {
        _actorRepository = actorRepository;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<List<ActorListDto>> Handle(GetActorsByTenantRequest request, CancellationToken cancellationToken)
    {
        var actors = await _actorRepository.GetActorsByTenant(request.TenantId, cancellationToken);
        var dtos = _mapper.Map<List<ActorListDto>>(actors);

        foreach (var dto in dtos)
        {
            var actor = actors.First(candidate => candidate.Id == dto.Id);
            dto.IsLocallyDiscoverable = true;
            ApplyPublicParticipationOverrides(actor, dto, request.TenantId);
            dto.ProfilePictureUri = await ResolveImageUrl(dto.ProfilePictureUri);
        }

        return dtos;
    }

    private static void ApplyPublicParticipationOverrides(Actor actor, ActorListDto dto, Guid tenantId)
    {
        var organization = actor.Organization?.TenantParticipations.SingleOrDefault(participation =>
            participation.TenantId == tenantId);
        var group = actor.Group?.TenantParticipations.SingleOrDefault(participation =>
            participation.TenantId == tenantId);

        dto.DisplayName = organization?.DisplayNameOverride
            ?? group?.DisplayNameOverride
            ?? dto.DisplayName;
        dto.ProfilePictureUri = PublicProfileImageUri(organization?.ProfilePicture)
            ?? PublicProfileImageUri(group?.ProfilePicture)
            ?? dto.ProfilePictureUri;
        dto.BackgroundColor = organization?.BackgroundColor ?? group?.BackgroundColor ?? dto.BackgroundColor;
        dto.BackgroundEffect = organization?.BackgroundEffect ?? group?.BackgroundEffect ?? dto.BackgroundEffect;
        dto.BannerColor = organization?.BannerColor ?? group?.BannerColor ?? dto.BannerColor;
    }

    private static string? PublicProfileImageUri(StorageObject? storageObject) =>
        storageObject is
        {
            IsDeleted: false,
            Visibility: StorageObjectVisibilities.PublicImage,
            LifecycleState: StorageObjectLifecycleStates.Active
        }
            ? storageObject.Uri
            : null;

    private Task<string?> ResolveImageUrl(string? objectKeyOrUri)
        => StoragePresentationUrlResolver.ResolveImageUrlAsync(
            objectKeyOrUri,
            _logger,
            "tenant actor profile image");
}
