using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class ExternalLoginAuthenticationRepository(
    IGlobalExploreDbContextFactory factory, PostgresNativeQueueEnrollment? enrollment)
    : IExternalLoginAuthenticationRepository
{
    public async Task<UserExternalLogin?> GetByProviderAndKey(
        ProviderAccountKey accountKey, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            bool relational = context.Database.IsRelational();
            if (relational) await context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await PostgresExternalLoginReadPermissions.RequireReadyAsync(context, enrollment, cancellationToken);
                return await context.UserExternalLogins.AsNoTracking().SingleOrDefaultAsync(
                    binding => binding.AuthenticationProviderId == (int)accountKey.ProviderKind
                        && binding.ProviderKey == accountKey.Value, cancellationToken);
            }
            finally
            {
                if (relational) await context.Database.CloseConnectionAsync();
            }
        });
    }
}
