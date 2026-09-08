using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Actor;
using Explore.Application.Features.Actors.Requests.Queries;
using Explore.Application.Services;
using Explore.Domain;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Actors.Handlers.Queries;

public class GetActorDetailsRequestHandler : IRequestHandler<GetActorDetailsRequest, ActorDto?>
{
    private readonly IActorRepository _actorRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IMapper _mapper;
    private readonly ILogger<GetActorDetailsRequestHandler> _logger;

    public GetActorDetailsRequestHandler(
        IActorRepository actorRepository,
        ITenantContext tenantContext,
        IMapper mapper,
        ILogger<GetActorDetailsRequestHandler> logger)
    {
        _actorRepository = actorRepository;
        _tenantContext = tenantContext;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<ActorDto?> Handle(GetActorDetailsRequest request, CancellationToken cancellationToken)
    {
        var actor = request.TenantId is { } tenantId
            ? await _actorRepository.GetPublicActorProfileByTenantAsync(tenantId, request.Id, cancellationToken)
            : await _actorRepository.GetPublicActorProfileAsync(request.Id, cancellationToken);
        if (actor is null)
        {
            return null;
        }

        var dto = _mapper.Map<ActorDto>(actor);
        if (dto is null)
        {
            return null;
        }

        if (request.TenantId is { } contextualTenantId)
        {
            dto.TenantId = contextualTenantId;
            dto.IsLocallyDiscoverable = true;
            ApplyPublicParticipationOverrides(actor, dto, contextualTenantId);
        }
        else
        {
            dto.IsLocallyDiscoverable = await _actorRepository.GetLocallyDiscoverableSubscriptionTargetAsync(
                _tenantContext.TenantId,
                dto.Id,
                cancellationToken) is not null;
        }

        dto.ProfilePictureUri = await ResolveImageUrl(dto.ProfilePictureUri);
        dto.BannerPictureUri = await ResolveImageUrl(dto.BannerPictureUri);
        dto.BackgroundImageUri = await ResolveImageUrl(dto.BackgroundImageUri);
        return dto;
    }

    private static void ApplyPublicParticipationOverrides(Actor actor, ActorDto dto, Guid tenantId)
    {
        var organization = actor.Organization?.TenantParticipations.SingleOrDefault(participation =>
            participation.TenantId == tenantId);
        var group = actor.Group?.TenantParticipations.SingleOrDefault(participation =>
            participation.TenantId == tenantId);

        dto.DisplayName = organization?.DisplayNameOverride
            ?? group?.DisplayNameOverride
            ?? dto.DisplayName;
        dto.Description = organization?.DescriptionOverride
            ?? group?.DescriptionOverride
            ?? dto.Description;
        dto.ProfilePictureUri = PublicProfileImageUri(organization?.ProfilePicture)
            ?? PublicProfileImageUri(group?.ProfilePicture)
            ?? dto.ProfilePictureUri;
        dto.BannerPictureUri = PublicProfileImageUri(organization?.BannerPicture)
            ?? PublicProfileImageUri(group?.BannerPicture)
            ?? dto.BannerPictureUri;
        dto.BackgroundImageUri = PublicProfileImageUri(organization?.BackgroundImage)
            ?? PublicProfileImageUri(group?.BackgroundImage)
            ?? dto.BackgroundImageUri;
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
            "actor detail profile image");
}
