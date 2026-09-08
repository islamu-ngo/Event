using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Requests.Commands;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Users.Handlers.Commands;

public class UpdateUserLastActiveTenantCommandHandler : IRequestHandler<UpdateUserLastActiveTenantCommand, bool>
{
    private readonly IUserRepository _userRepository;
    private readonly ITenantUserRepository _tenantUserRepository;
    private readonly HybridCache _cache;

    public UpdateUserLastActiveTenantCommandHandler(
        IUserRepository userRepository,
        ITenantUserRepository tenantUserRepository,
        HybridCache cache)
    {
        _userRepository = userRepository;
        _tenantUserRepository = tenantUserRepository;
        _cache = cache;
    }

    public async Task<bool> Handle(UpdateUserLastActiveTenantCommand request, CancellationToken cancellationToken)
    {
        // 1. Verify user is actually a member of the target tenant
        var isActive = await _tenantUserRepository.IsActiveTenantUserAsync(request.TenantId, request.UserId, cancellationToken);
        if (!isActive)
        {
            return false;
        }

        // 2. Load user
        var user = await _userRepository.GetById(request.UserId);
        if (user == null)
        {
            return false;
        }

        // 3. Update LastActiveTenantId
        if (user.LastActiveTenantId == request.TenantId)
        {
            return true; // Already up to date
        }

        user.LastActiveTenantId = request.TenantId;
        await _userRepository.Update(user);

        // 4. Invalidate user cache
        await _cache.RemoveAsync($"user:detail:{request.UserId}", cancellationToken);

        return true;
    }
}
