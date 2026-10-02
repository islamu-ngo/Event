using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Actor;
using Explore.Application.Features.Actors.Requests.Queries;
using Explore.Application.Contracts.Operations;
using Explore.Application.Services;
using Explore.Domain;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Actors.Handlers.Queries;

public class GetActorsByTenantRequestHandler : IQueryHandler<GetActorsByTenantRequest, List<ActorListDto>>
{
    private readonly IActorRepository _actorRepository;
    private readonly ILogger<GetActorsByTenantRequestHandler> _logger;

    public GetActorsByTenantRequestHandler(
        IActorRepository actorRepository,
        ILogger<GetActorsByTenantRequestHandler> logger)
    {
        _actorRepository = actorRepository;
        _logger = logger;
    }

    public async Task<List<ActorListDto>> QueryAsync(GetActorsByTenantRequest request, CancellationToken cancellationToken = default)
    {
        var actors = await _actorRepository.GetActorsByTenant(request.TenantId, cancellationToken);
        var dtos = actors.Select(ActorFederationMapper.ToActorListItem).ToList();

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
        var profilePicture = StoragePresentationUrlResolver.PublicProfileImageUri(organization?.ProfilePicture, tenantId) is not null
            ? organization!.ProfilePicture
            : StoragePresentationUrlResolver.PublicProfileImageUri(group?.ProfilePicture, tenantId) is not null
                ? group!.ProfilePicture
                : null;
        if (profilePicture is not null)
        {
            dto.ProfilePictureStorageObjectId = profilePicture.Id;
            dto.ExternalProfilePictureUri = null;
            dto.ProfilePictureUri = StoragePresentationUrlResolver.PublicProfileImageUri(profilePicture, tenantId);
        }
        dto.BackgroundColor = organization?.BackgroundColor ?? group?.BackgroundColor ?? dto.BackgroundColor;
        dto.BackgroundEffect = organization?.BackgroundEffect ?? group?.BackgroundEffect ?? dto.BackgroundEffect;
        dto.BannerColor = organization?.BannerColor ?? group?.BannerColor ?? dto.BannerColor;
    }

    private Task<string?> ResolveImageUrl(string? objectKeyOrUri)
        => StoragePresentationUrlResolver.ResolveImageUrlAsync(
            objectKeyOrUri,
            _logger,
            "tenant actor profile image");
}
