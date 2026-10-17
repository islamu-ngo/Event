using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Repositories;

public sealed class ExternalLoginAuthenticationRepository(IGlobalExploreDbContextFactory factory)
    : IExternalLoginAuthenticationRepository
{
    public async Task<UserExternalLogin?> GetByProviderAndKey(
        ProviderAccountKey accountKey, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await ExternalLoginAuthenticationQuery.ReadAsync(context, accountKey, cancellationToken);
    }
}
