using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Repositories;

/// <summary>Separate Global read of a resolved user's live navigation preference; never loads identity data.</summary>
public sealed class UserTenantNavigationRepository(
    IGlobalExploreDbContextFactory globalFactory,
    IServiceProvider services) : IUserTenantNavigationRepository
{
    public async Task<User?> GetNavigationUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await UserTenantNavigationQuery.ReadAsync(context, services, userId, cancellationToken);
    }
}
