using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Organization;
using Explore.Application.Features.Organizations.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Organizations.Handlers.Queries;

public class GetMyOrganizationsRequestHandler : IRequestHandler<GetMyOrganizationsRequest, PaginatedResult<OrganizationListDto>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IMapper _mapper;
    private readonly IObjectStorageService _objectStorageService;
    private readonly ILogger<GetMyOrganizationsRequestHandler> _logger;

    public GetMyOrganizationsRequestHandler(
        IOrganizationRepository organizationRepository,
        IOrganizationMemberRepository organizationMemberRepository,
        IMapper mapper,
        IObjectStorageService objectStorageService,
        ILogger<GetMyOrganizationsRequestHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _organizationMemberRepository = organizationMemberRepository;
        _mapper = mapper;
        _objectStorageService = objectStorageService;
        _logger = logger;
    }

    public async Task<PaginatedResult<OrganizationListDto>> Handle(GetMyOrganizationsRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.UserId, out Guid userGuid))
        {
            return PaginatedResult<OrganizationListDto>.Create(new List<OrganizationListDto>(), 0, request.PageNumber, request.PageSize);
        }

        // Get paginated organizations for the user
        var (organizations, totalCount) = await _organizationRepository.GetMyOrganizationsPaged(userGuid, request.PageNumber, request.PageSize, cancellationToken);

        // Get memberships to add user role info
        var memberships = await _organizationMemberRepository.GetMembershipsByUser(userGuid, cancellationToken);
        var membershipDict = memberships.ToDictionary(m => m.OrganizationTenant.OrganizationId, m => m.RoleId);

        // Map OrganizationMember entities to OrganizationListDto
        var dtos = new List<OrganizationListDto>();
        foreach (var org in organizations)
        {
            var dto = _mapper.Map<OrganizationListDto>(org);
            if (membershipDict.TryGetValue(org.Id, out var roleId))
            {
                dto.CurrentUserRoleId = roleId;
            }
            // Resolve presigned URL for profile picture
            dto.ActorProfilePictureUri = await ResolveImageUrl(dto.ActorProfilePictureUri);
            dtos.Add(dto);
        }

        return PaginatedResult<OrganizationListDto>.Create(dtos, totalCount, request.PageNumber, request.PageSize);
    }

    private Task<string?> ResolveImageUrl(string? objectKeyOrUri)
        => StoragePresentationUrlResolver.ResolveImageUrlAsync(
            objectKeyOrUri,
            _logger,
            "my organizations profile image");
}
