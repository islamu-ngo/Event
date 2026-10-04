using System;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Services;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Users.Handlers.Queries;

public class GetUserRequestHandler : IQueryHandler<GetUserRequest, UserDto?>
{
    private readonly IUserRepository _userRepository;
    private readonly IObjectStorageService _objectStorageService;
    private readonly ILogger<GetUserRequestHandler> _logger;
    private readonly HybridCache _cache;
    private readonly IPrivacyErasureStateRepository _privacyErasureStateRepository;

    public GetUserRequestHandler(
        IUserRepository userRepository,
        IObjectStorageService objectStorageService,
        ILogger<GetUserRequestHandler> logger,
        HybridCache cache,
        IPrivacyErasureStateRepository privacyErasureStateRepository)
    {
        _userRepository = userRepository;
        _objectStorageService = objectStorageService;
        _logger = logger;
        _cache = cache;
        _privacyErasureStateRepository = privacyErasureStateRepository;
    }

    public async Task<UserDto?> QueryAsync(GetUserRequest request, CancellationToken cancellationToken = default)
    {
        if (await _privacyErasureStateRepository.GetBySubjectAsync(request.UserId, cancellationToken) is not null)
        {
            return null;
        }

        var cacheKey = $"user:detail:{request.UserId}";

        var userDto = await _cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                var user = await _userRepository.GetUserWithDetails(request.UserId, _);
                if (user == null)
                {
                    return null;
                }

                var dto = UserMapper.ToDetail(user);

                return dto;
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            },
            cancellationToken: cancellationToken);

        if (await _privacyErasureStateRepository.GetBySubjectAsync(request.UserId, cancellationToken) is not null)
        {
            await _cache.RemoveAsync(cacheKey, cancellationToken);
            return null;
        }

        if (userDto is not null)
        {
            // The account cache is global; managed media is tenant-filtered and can be revoked independently.
            // Re-project media from the current entity graph rather than caching image publication authority.
            var currentUser = await _userRepository.GetUserWithDetails(request.UserId, cancellationToken);
            if (currentUser is null)
                return null;
            userDto = userDto with
            {
                ProfilePictureStorageObjectId = StoragePresentationUrlResolver.ManagedProfilePictureId(currentUser.Actor?.Pii),
                ExternalProfilePictureUri = StoragePresentationUrlResolver.ExternalProfilePictureUri(currentUser.Actor?.Pii),
                ProfileImageUri = StoragePresentationUrlResolver.ActorProfilePictureUri(currentUser.Actor?.Pii)
            };
        }

        return userDto;
    }

}
