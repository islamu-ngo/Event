using System.Runtime.ExceptionServices;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>Projects ownership identifiers on the caller's original selected tenant context.</summary>
public sealed class ActorOwnershipReader(
    ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment) : IActorOwnershipReader
{
    public async Task<ActorOwnershipFacts?> ReadForUserAsync(
        Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);
        context.RequireBoundTenantLease();
        var contextId = context.ContextId;
        var tenantLease = context.TenantContext;
        var transaction = context.Database.CurrentTransaction;
        void RequireOriginalTenant()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (tenantId == Guid.Empty || context.TenantFilterTenantId != tenantId
                || context.IsTenantFilterBypassed || context.ContextId != contextId
                || !ReferenceEquals(context.TenantContext, tenantLease)
                || !ReferenceEquals(context.Database.CurrentTransaction, transaction))
                throw new UnauthorizedAccessException("actor_ownership_requires_original_selected_tenant");
        }

        RequireOriginalTenant();
        bool relational = context.Database.IsRelational();
        bool opened = false;
        bool initiallyClosed = false;
        bool observedOpen = false;
        RelationalConnectionIdentity? connection = null;
        bool interrupted = false;
        bool reopened = false;
        int? backend = null;
        void Changed(object? sender, System.Data.StateChangeEventArgs change)
        {
            if (initiallyClosed && !observedOpen)
            {
                if (change.CurrentState == System.Data.ConnectionState.Connecting) return;
                if (change.CurrentState == System.Data.ConnectionState.Open)
                {
                    observedOpen = true;
                    backend = connection!.BackendProcessId;
                    return;
                }
            }
            if (change.CurrentState != System.Data.ConnectionState.Open) interrupted = true;
            else if (interrupted) reopened = true;
        }
        bool IsOriginalOwner() => connection is not null
            && context.ContextId == contextId && connection.Matches(context)
            && ReferenceEquals(context.Database.CurrentTransaction, transaction);
        bool CanReleaseOriginal() => IsOriginalOwner() && !interrupted
            && connection!.State == System.Data.ConnectionState.Open && connection.BackendProcessId == backend;
        bool CanBalanceOpening() => CanReleaseOriginal()
            || (IsOriginalOwner() && connection!.State == System.Data.ConnectionState.Closed && !reopened);
        void RequireOriginal()
        {
            RequireOriginalTenant();
            if (connection is not null && (interrupted || !connection.Matches(context)
                || connection.State != System.Data.ConnectionState.Open || connection.BackendProcessId != backend))
                throw new UnauthorizedAccessException("actor_ownership_original_connection_changed");
        }
        ActorOwnershipFacts? result = null;
        Exception? failure = null;
        try
        {
            if (relational)
            {
                connection = RelationalConnectionIdentity.Capture(context);
                initiallyClosed = connection.State == System.Data.ConnectionState.Closed;
                observedOpen = connection.State == System.Data.ConnectionState.Open;
                backend = observedOpen ? connection.BackendProcessId : null;
                connection.StateChange += Changed;
                await context.Database.OpenConnectionAsync(cancellationToken);
                opened = true;
                RequireOriginalTenant();
            }
            result = await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                RequireOriginal();
                await PostgresActorOwnershipReadPermissions.RequireReadyAsync(context, enrollment, cancellationToken);
                RequireOriginal();
                var facts = await context.Actors.IgnoreAutoIncludes().AsNoTracking()
                    .Where(actor => actor.UserId == userId && context.TenantUsers.Any(participation =>
                        participation.TenantId == tenantId && participation.ActorId == actor.Id))
                    .Select(actor => new ActorOwnershipFacts(actor.Id, actor.UserId, actor.GroupId, actor.OrganizationId))
                    .FirstOrDefaultAsync(cancellationToken);
                RequireOriginal();
                return facts;
            });
        }
        catch (Exception error) { failure = error; }
        try
        {
            if (opened)
            {
                if (!CanBalanceOpening())
                    throw new UnauthorizedAccessException("actor_ownership_original_connection_changed");
                // Even a broken original connector retains this successful EF opening's count.
                await context.Database.CloseConnectionAsync();
            }
            else if (initiallyClosed && observedOpen && CanReleaseOriginal())
            {
                // A failing ConnectionOpened interceptor leaves EF's open count unchanged.
                await connection!.CloseAfterFailedOpenAsync();
            }
        }
        catch (Exception cleanup)
        {
            if (failure is not null)
                throw new AggregateException("actor_ownership_read_and_cleanup_failed", failure, cleanup);
            throw;
        }
        finally
        {
            if (connection is not null) connection.StateChange -= Changed;
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }
}
