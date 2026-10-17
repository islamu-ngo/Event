using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace SecurityTestSupport;

internal sealed class AdministrativeAuthorityTestFactory(ExploreDbContext source) : IGlobalExploreDbContextFactory
{
    public async Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = (DbContextOptions<ExploreDbContext>)source.GetService<IDbContextOptions>();
        var context = new ExploreDbContext(options);
        if (source.Database.CurrentTransaction is { } transaction)
            await context.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
        return context;
    }
}
