using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

/// <summary>Reads detached bootstrap evidence from the independent ordinary Global pool.</summary>
public sealed class InstanceBootstrapStateReader(
    IGlobalExploreDbContextFactory globalFactory, IServiceProvider services) : IInstanceBootstrapStateReader
{
    public async Task<InstanceBootstrapState?> GetCurrent(CancellationToken cancellationToken = default)
    {
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await InstanceBootstrapStateQuery.ReadAsync(
            context, () => services.GetRequiredService<PostgresNativeQueueEnrollment>(), cancellationToken);
    }
}
