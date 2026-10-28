using System.Data;
using System.Data.Common;
using System.Collections.Immutable;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class EventAuthorityProviderOperations
{
    internal static Task<AuthorityPlan> ReadPlanAsync(
        ExploreDbContext context, Guid tenantId, Guid[] userIds, Guid[] eventIds, CancellationToken cancellationToken,
        bool includeOwners = false) =>
        ReadWithoutRetainingLocksAsync(context, async source => new AuthorityPlan(
            await ReadOwnershipAsync(source, tenantId, eventIds, cancellationToken),
            await ReadAssignmentsAsync(source, tenantId, userIds, eventIds, cancellationToken, includeOwners)), cancellationToken);

    internal static Task<ResourceAncestry?> ReadResourcePlanAsync(
        ExploreDbContext context, Guid tenantId, Guid resourceId, CancellationToken cancellationToken) =>
        ReadWithoutRetainingLocksAsync<ResourceAncestry?>(context, async source =>
        {
            var resource = await source.Set<EventResource>().AsNoTracking()
                .Where(row => row.Id == resourceId && row.TenantId == tenantId && !row.IsDeleted)
                .Select(row => new { row.EventId, row.StorageObjectId }).SingleOrDefaultAsync(cancellationToken);
            if (resource is null) return null;
            var parent = await source.Events.AsNoTracking()
                .Where(row => row.Id == resource.EventId && row.TenantId == tenantId && !row.IsDeleted)
                .Select(row => new Ownership(row.Id, row.ActorId, row.OrganizerActorId)).SingleOrDefaultAsync(cancellationToken);
            if (parent is null) return null;
            var storageActor = resource.StorageObjectId is { } storageId
                ? await source.Set<StorageObject>().AsNoTracking()
                    .Where(row => row.Id == storageId && row.TenantId == tenantId && !row.IsDeleted)
                    .Select(row => row.ActorId).SingleOrDefaultAsync(cancellationToken)
                : null;
            return new(resourceId, parent.Id, parent.ActorId, parent.OrganizerActorId, resource.StorageObjectId, storageActor);
        }, cancellationToken);

    internal static Task<ResourceAncestry> ReadResourcePlanAsync(
        ExploreDbContext context, ModerationResourceGeneration generation, CancellationToken cancellationToken) =>
        ReadWithoutRetainingLocksAsync<ResourceAncestry>(context, async source =>
        {
            // Retained native parents survive receipt replay; only storage ownership is planned live.
            var storageActor = generation.StorageObjectId is { } storageId
                ? await source.Set<StorageObject>().AsNoTracking()
                    .Where(row => row.Id == storageId && row.TenantId == generation.TenantId && !row.IsDeleted)
                    .Select(row => row.ActorId).SingleOrDefaultAsync(cancellationToken)
                : null;
            return new(generation.ResourceId, generation.EventId, generation.ActorId, generation.OrganizerActorId,
                generation.StorageObjectId, storageActor);
        }, cancellationToken);

    internal static Task<IReadOnlyDictionary<Guid, Ownership>> ReadMaterialGenerationPlansAsync(
        ExploreDbContext context, Guid hostId, Guid tenantId, CancellationToken token,
        Guid? requestedGenerationId = null, Guid? eventId = null) =>
        ReadWithoutRetainingLocksAsync<IReadOnlyDictionary<Guid, Ownership>>(context, async source =>
        {
            var generations = await source.ModerationEventGenerations.AsNoTracking()
                .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId
                    && (eventId == null || row.EventId == eventId)
                    && (row.Id == requestedGenerationId || !source.ModerationEventGenerations.Any(newer =>
                        newer.HostBindingId == hostId && newer.TenantId == tenantId
                        && newer.EventId == row.EventId && newer.Revision > row.Revision)))
                .Select(row => new { row.Id, row.EventId, row.ActorId, row.OrganizerActorId }).ToArrayAsync(token);
            return generations.ToImmutableDictionary(row => row.Id,
                row => new Ownership(row.EventId, row.ActorId, row.OrganizerActorId));
        }, token);

    internal static async Task<T> ReadWithoutRetainingLocksAsync<T>(
        ExploreDbContext context, Func<ExploreDbContext, Task<T>> read, CancellationToken cancellationToken)
    {
        if (context.Database.ProviderName is not
            (RelationalNamedLock.SqlServerProvider or RelationalNamedLock.MySqlProvider))
            return await read(context);

        // Retaining-read providers must not lock native ancestry or grants while planning
        // their earlier Role and Actor anchors. Dirty keys only propose a fence set.
        var options = (DbContextOptions<ExploreDbContext>)context.GetService<IDbContextOptions>();
        var relational = options.Extensions.OfType<RelationalOptionsExtension>().Single();
        await using var connection = DbProviderFactories.GetFactory(context.Database.GetDbConnection())?.CreateConnection()
            ?? throw new InvalidOperationException("Event authority planning requires a provider connection factory.");
        connection.ConnectionString = relational.ConnectionString ?? context.Database.GetDbConnection().ConnectionString;
        var builder = new DbContextOptionsBuilder<ExploreDbContext>(options);
        ((IDbContextOptionsBuilderInfrastructure)builder)
            .AddOrUpdateExtension(relational.WithConnection(connection, false));
        await using var planning = new ExploreDbContext(builder.Options)
        {
            TenantContext = context.TenantContext,
            ModerationTrustDomainId = context.ModerationTrustDomainId
        };
        return await planning.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await planning.Database.BeginTransactionAsync(
                IsolationLevel.ReadUncommitted, cancellationToken);
            var plan = await read(planning);
            await transaction.CommitAsync(cancellationToken);
            return plan;
        });
    }

    internal static Task<List<Ownership>> ReadOwnershipAsync(
        ExploreDbContext context, Guid tenantId, Guid[] eventIds, CancellationToken cancellationToken) =>
        context.Events.AsNoTracking()
            .Where(parent => parent.TenantId == tenantId && eventIds.Contains(parent.Id))
            .Select(parent => new Ownership(parent.Id, parent.ActorId, parent.OrganizerActorId))
            .ToListAsync(cancellationToken);

    internal static Task<List<Assignment>> ReadAssignmentsAsync(
        ExploreDbContext context, Guid tenantId, Guid[] userIds, Guid[] eventIds, CancellationToken cancellationToken,
        bool includeOwners = false) =>
        context.EventRoleAssignments.AsNoTracking()
            .Where(row => row.TenantId == tenantId && eventIds.Contains(row.EventId)
                && (userIds.Contains(row.UserId) || includeOwners && row.RoleId == (int)RoleEnum.EventOwner))
            .Select(row => new Assignment(row.Id, row.UserId, row.EventId, row.RoleId))
            .ToListAsync(cancellationToken);

    internal sealed record AuthorityPlan(List<Ownership> Parents, List<Assignment> Assignments);
    internal sealed record Assignment(Guid Id, Guid UserId, Guid EventId, int RoleId);
    internal sealed record Ownership(Guid Id, Guid ActorId, Guid? OrganizerActorId);
    internal sealed record ResourceAncestry(Guid ResourceId, Guid EventId, Guid ActorId, Guid? OrganizerActorId,
        Guid? StorageObjectId, Guid? StorageActorId);
}
