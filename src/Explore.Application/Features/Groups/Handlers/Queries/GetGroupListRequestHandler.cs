using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Group;
using Explore.Application.Features.Groups.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Groups.Handlers.Queries;

public class GetGroupListRequestHandler : IRequestHandler<GetGroupListRequest, PaginatedResult<GroupListDto>>
{
    private readonly IGroupRepository _groupRepository;
    private readonly IMapper _mapper;
    private readonly IObjectStorageService _objectStorageService;
    private readonly ILogger<GetGroupListRequestHandler> _logger;

    public GetGroupListRequestHandler(
        IGroupRepository groupRepository,
        IMapper mapper,
        IObjectStorageService objectStorageService,
        ILogger<GetGroupListRequestHandler> logger)
    {
        _groupRepository = groupRepository;
        _mapper = mapper;
        _objectStorageService = objectStorageService;
        _logger = logger;
    }

    public async Task<PaginatedResult<GroupListDto>> Handle(GetGroupListRequest request, CancellationToken cancellationToken)
    {
        var (groups, totalCount) = await _groupRepository.GetGroupsWithDetailsPaged(request.PageNumber, request.PageSize);
        var groupDtos = _mapper.Map<List<GroupListDto>>(groups);

        foreach (var dto in groupDtos)
        {
            dto.ActorProfilePictureUri = await ResolveImageUrl(dto.ActorProfilePictureUri);
        }

        return PaginatedResult<GroupListDto>.Create(groupDtos, totalCount, request.PageNumber, request.PageSize);
    }

    private Task<string?> ResolveImageUrl(string? objectKeyOrUri)
        => StoragePresentationUrlResolver.ResolveImageUrlAsync(
            objectKeyOrUri,
            _logger,
            "group list profile image");
}
