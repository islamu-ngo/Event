using Explore.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed class IdentitySynchronizationBootstrapReader(ExploreDbContext context)
    : IIdentitySynchronizationBootstrapReader
{
    public Task<IdentitySynchronizationBootstrapState?> GetCurrentAsync(CancellationToken cancellationToken) =>
        context.InstanceBootstrapStates.IgnoreAutoIncludes().AsNoTracking()
            .OrderByDescending(state => state.Generation)
            .ThenByDescending(state => state.CreatedAt)
            .ThenByDescending(state => state.Id)
            .Select(state => new IdentitySynchronizationBootstrapState(
                state.Status, state.Mode, state.ProviderKind, state.CompletedByUserId))
            .FirstOrDefaultAsync(cancellationToken);
}
