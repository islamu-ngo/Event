using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class ExternalLoginAuthenticationQuery
{
    internal static async Task<UserExternalLogin?> ReadAsync(
        ExploreDbContext context, ProviderAccountKey accountKey, CancellationToken cancellationToken)
    {
        IQueryable<UserExternalLogin> bindings = context.UserExternalLogins;
        if (context.Database.IsNpgsql())
        {
            string command = $$"""
                SELECT * FROM {{PostgresExternalLoginAuthenticationContract.FunctionSql(context)}}({0}, {1})
                """;
            bindings = context.UserExternalLogins.FromSqlRaw(
                command, (int)accountKey.ProviderKind, accountKey.Value);
        }

        return await bindings.AsNoTracking().SingleOrDefaultAsync(
            binding => binding.AuthenticationProviderId == (int)accountKey.ProviderKind
                && binding.ProviderKey == accountKey.Value,
            cancellationToken);
    }
}
