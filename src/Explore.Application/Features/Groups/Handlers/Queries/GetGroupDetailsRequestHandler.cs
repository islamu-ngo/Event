using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Group;
using Explore.Application.Features.Groups.Requests.Queries;
using Explore.Application.Services;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Groups.Handlers.Queries;

public class GetGroupDetailsRequestHandler : IRequestHandler<GetGroupDetailsRequest, GroupDto>
{
    private readonly IGroupRepository _groupRepository;
    private readonly IMapper _mapper;
    private readonly IObjectStorageService _objectStorageService;
    private readonly ILogger<GetGroupDetailsRequestHandler> _logger;
    private readonly HybridCache _cache;

    public GetGroupDetailsRequestHandler(
        IGroupRepository groupRepository,
        IMapper mapper,
        IObjectStorageService objectStorageService,
        ILogger<GetGroupDetailsRequestHandler> logger,
        HybridCache cache)
    {
        _groupRepository = groupRepository;
        _mapper = mapper;
        _objectStorageService = objectStorageService;
        _logger = logger;
        _cache = cache;
    }

    public async Task<GroupDto> Handle(GetGroupDetailsRequest request, CancellationToken cancellationToken)
    {
        var cacheKey = $"group:detail:{request.Id}";
        var dto = await _cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                var group = await _groupRepository.GetGroupWithDetails(request.Id);
                return _mapper.Map<GroupDto>(group);
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            },
            cancellationToken: cancellationToken);

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
            "group detail profile image");
}
