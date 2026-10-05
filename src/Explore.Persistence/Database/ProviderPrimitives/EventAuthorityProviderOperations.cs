using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class EventAuthorityProviderOperations
{
    internal static async Task<List<Ownership>> ReadOwnershipPlanAsync(
        ExploreDbContext context, Guid tenantId, Guid[] eventIds, CancellationToken cancellationToken)
    {
        if (context.Database.ProviderName is not
            (RelationalNamedLock.SqlServerProvider or RelationalNamedLock.MySqlProvider))
            return await ReadOwnershipAsync(context, tenantId, eventIds, cancellationToken);

        // SQL Server and InnoDB retain Serializable shared Event locks. Planning
        // cannot take those locks before Actor anchors, nor wait on an earlier
        // save by this transaction. These dirty keys are only a proposed fence set;
        // the owning transaction must validate them under Actor then Event fences.
        var options = (DbContextOptions<ExploreDbContext>)context.GetService<IDbContextOptions>();
        var relational = options.Extensions.OfType<RelationalOptionsExtension>().Single();
        await using var connection = DbProviderFactories.GetFactory(context.Database.GetDbConnection())?.CreateConnection()
            ?? throw new InvalidOperationException("Event authority planning requires a provider connection factory.");
        connection.ConnectionString = relational.ConnectionString ?? context.Database.GetDbConnection().ConnectionString;
        var builder = new DbContextOptionsBuilder<ExploreDbContext>(options);
        ((IDbContextOptionsBuilderInfrastructure)builder)
            .AddOrUpdateExtension(relational.WithConnection(connection, false));
        await using var planning = new ExploreDbContext(builder.Options) { TenantContext = context.TenantContext };
        return await planning.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await planning.Database.BeginTransactionAsync(
                IsolationLevel.ReadUncommitted, cancellationToken);
            var owners = await ReadOwnershipAsync(planning, tenantId, eventIds, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return owners;
        });
    }

    internal static Task<List<Ownership>> ReadOwnershipAsync(
        ExploreDbContext context, Guid tenantId, Guid[] eventIds, CancellationToken cancellationToken) =>
        context.Events.AsNoTracking()
            .Where(parent => parent.TenantId == tenantId && eventIds.Contains(parent.Id))
            .Select(parent => new Ownership(parent.Id, parent.ActorId, parent.OrganizerActorId))
            .ToListAsync(cancellationToken);

    internal sealed record Ownership(Guid Id, Guid ActorId, Guid? OrganizerActorId);
}
