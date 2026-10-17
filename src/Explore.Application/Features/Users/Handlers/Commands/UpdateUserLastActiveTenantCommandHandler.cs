using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Users.Requests.Commands;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Users.Handlers.Commands;

public class UpdateUserLastActiveTenantCommandHandler : ICommandHandler<UpdateUserLastActiveTenantCommand, bool>
{
    private readonly IHumanTenantPreferencePreparation _preparation;
    private readonly HybridCache _cache;
    private readonly IUserContext _userContext;
    private readonly IAdminContext _adminContext;

    public UpdateUserLastActiveTenantCommandHandler(
        IHumanTenantPreferencePreparation preparation,
        HybridCache cache,
        IUserContext userContext,
        IAdminContext adminContext)
    {
        _preparation = preparation;
        _cache = cache;
        _userContext = userContext;
        _adminContext = adminContext;
    }

    public async Task<bool> ExecuteAsync(UpdateUserLastActiveTenantCommand request, CancellationToken cancellationToken = default)
    {
        if (!_userContext.IsAuthenticated || request.UserId == Guid.Empty || request.TenantId == Guid.Empty
            || await _adminContext.ResolveUserIdAsync(cancellationToken) != request.UserId)
            return false;
        if (!await _preparation.ExecuteAsync(request, cancellationToken))
            return false;
        await _cache.RemoveAsync($"user:detail:{request.UserId}", cancellationToken);

        return true;
    }
}
