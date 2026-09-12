using System;
using System.Collections.Generic;
using System.Text;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Organization;
using Explore.Application.Features.Organizations.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Organizations.Handlers.Queries;

public class GetOrganizationListRequestHandler : IQueryHandler<GetOrganizationListRequest, PaginatedResult<OrganizationListDto>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IObjectStorageService _objectStorageService;
    private readonly ILogger<GetOrganizationListRequestHandler> _logger;

    public GetOrganizationListRequestHandler(
        IOrganizationRepository organizationRepository,
        IObjectStorageService objectStorageService,
        ILogger<GetOrganizationListRequestHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _objectStorageService = objectStorageService;
        _logger = logger;
    }

    public async Task<PaginatedResult<OrganizationListDto>> QueryAsync(GetOrganizationListRequest request, CancellationToken cancellationToken)
    {
        // Get organizations with ApprovalStatus for admin purposes
        var (organizations, totalCount) = await _organizationRepository.GetOrganizationsWithDetailsPaged(request.PageNumber, request.PageSize, cancellationToken);
        var organizationDtos = organizations.Select(OrganizationMapper.ToOrganizationListItem).ToList();

        // Normalize public profile image references.
        foreach (var dto in organizationDtos)
        {
            dto.ActorProfilePictureUri = await ResolveImageUrl(dto.ActorProfilePictureUri);
        }

        return PaginatedResult<OrganizationListDto>.Create(organizationDtos, totalCount, request.PageNumber, request.PageSize);
    }

    private Task<string?> ResolveImageUrl(string? objectKeyOrUri)
        => StoragePresentationUrlResolver.ResolveImageUrlAsync(
            objectKeyOrUri,
            _logger,
            "organization list profile image");
}
