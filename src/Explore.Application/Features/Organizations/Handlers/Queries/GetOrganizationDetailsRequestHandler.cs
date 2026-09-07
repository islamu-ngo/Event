using System;
using System.Collections.Generic;
using System.Text;
using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Organization;
using Explore.Application.Features.Organizations.Requests.Queries;
using Explore.Application.Services;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Organizations.Handlers.Queries;

public class GetOrganizationDetailsRequestHandler : IRequestHandler<GetOrganizationDetailsRequest, OrganizationDto?>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IMapper _mapper;
    private readonly IObjectStorageService _objectStorageService;
    private readonly ILogger<GetOrganizationDetailsRequestHandler> _logger;
    private readonly HybridCache _cache;

    public GetOrganizationDetailsRequestHandler(
        IOrganizationRepository organizationRepository,
        IMapper mapper,
        IObjectStorageService objectStorageService,
        ILogger<GetOrganizationDetailsRequestHandler> logger,
        HybridCache cache)
    {
        _organizationRepository = organizationRepository;
        _mapper = mapper;
        _objectStorageService = objectStorageService;
        _logger = logger;
        _cache = cache;
    }

    public async Task<OrganizationDto?> Handle(GetOrganizationDetailsRequest request, CancellationToken cancellationToken)
    {
        var cacheKey = $"organization:detail:{request.Id}";
        var dto = await _cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                var organization = await _organizationRepository.GetOrganizationWithDetails(request.Id, _)
                    ?? await _organizationRepository.GetOrganizationWithDetailsByActorId(request.Id, _);

                return organization is null ? null : _mapper.Map<OrganizationDto>(organization);
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            },
            cancellationToken: cancellationToken);

        // Resolve presigned URL for profile picture
        if (dto != null)
        {
            dto.ActorProfilePictureUri = await ResolveImageUrl(dto.ActorProfilePictureUri);
        }

        return dto;
    }

    private Task<string?> ResolveImageUrl(string? objectKeyOrUri)
        => StoragePresentationUrlResolver.ResolveImageUrlAsync(
            objectKeyOrUri,
            _logger,
            "organization detail profile image");
}
