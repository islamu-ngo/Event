using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Group;
using Explore.Application.Features.Groups.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Groups.Handlers.Queries;

public class GetMyGroupsRequestHandler : IRequestHandler<GetMyGroupsRequest, PaginatedResult<GroupListDto>>
{
    private readonly IGroupRepository _groupRepository;
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IMapper _mapper;
    private readonly IObjectStorageService _objectStorageService;
    private readonly ILogger<GetMyGroupsRequestHandler> _logger;

    public GetMyGroupsRequestHandler(
        IGroupRepository groupRepository,
        IGroupMemberRepository groupMemberRepository,
        IMapper mapper,
        IObjectStorageService objectStorageService,
        ILogger<GetMyGroupsRequestHandler> logger)
    {
        _groupRepository = groupRepository;
        _groupMemberRepository = groupMemberRepository;
        _mapper = mapper;
        _objectStorageService = objectStorageService;
        _logger = logger;
    }

    public async Task<PaginatedResult<GroupListDto>> Handle(GetMyGroupsRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.UserId, out Guid userGuid))
        {
            return PaginatedResult<GroupListDto>.Create(new List<GroupListDto>(), 0, request.PageNumber, request.PageSize);
        }

        var (groups, totalCount) = await _groupRepository.GetMyGroupsPaged(userGuid, request.PageNumber, request.PageSize);

        var memberships = await _groupMemberRepository.GetMembershipsByUser(userGuid, cancellationToken);
        var membershipDict = memberships.ToDictionary(m => m.GroupTenant.GroupId, m => m.RoleId);

        var dtos = new List<GroupListDto>();
        foreach (var group in groups)
        {
            var dto = _mapper.Map<GroupListDto>(group);
            if (membershipDict.TryGetValue(group.Id, out var roleId))
            {
                dto.CurrentUserRoleId = roleId;
            }
            dto.ActorProfilePictureUri = await ResolveImageUrl(dto.ActorProfilePictureUri);
            dtos.Add(dto);
        }

        return PaginatedResult<GroupListDto>.Create(dtos, totalCount, request.PageNumber, request.PageSize);
    }

    private Task<string?> ResolveImageUrl(string? objectKeyOrUri)
        => StoragePresentationUrlResolver.ResolveImageUrlAsync(
            objectKeyOrUri,
            _logger,
            "my groups profile image");
}
