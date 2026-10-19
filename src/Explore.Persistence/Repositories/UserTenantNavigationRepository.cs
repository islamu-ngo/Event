using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>Separate Global read of a resolved user's live navigation preference; never loads identity data.</summary>
public sealed class UserTenantNavigationRepository(
    IGlobalExploreDbContextFactory globalFactory,
    PostgresNativeQueueEnrollment? enrollment) : IUserTenantNavigationRepository
{
    public async Task<User?> GetNavigationUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.RequireBoundTenantLease();
            if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.Database.CurrentTransaction is not null)
                throw new UnauthorizedAccessException("navigation_requires_selected_global_owner");
            bool relational = context.Database.IsRelational();
            if (relational) await context.Database.OpenConnectionAsync(cancellationToken);
            var connection = relational ? RelationalConnectionIdentity.Capture(context) : null;
            var contextId = context.ContextId;
            int? backend = connection?.BackendProcessId;
            bool interrupted = false;
            void OnStateChange(object? sender, System.Data.StateChangeEventArgs args)
            {
                if (args.CurrentState != System.Data.ConnectionState.Open) interrupted = true;
            }
            void RequireOriginal()
            {
                cancellationToken.ThrowIfCancellationRequested();
                context.RequireBoundTenantLease();
                if (context.ContextId != contextId || context.TenantFilterTenantId is not null
                    || context.IsTenantFilterBypassed || context.Database.CurrentTransaction is not null
                    || (connection is not null && (interrupted || !connection.Matches(context)
                        || connection.State != System.Data.ConnectionState.Open || connection.BackendProcessId != backend)))
                    throw new UnauthorizedAccessException("navigation_original_owner_changed");
            }
            if (connection is not null) connection.StateChange += OnStateChange;
            try
            {
                RequireOriginal();
                await PostgresUserTenantNavigationReadPermissions.RequireReadyAsync(context, enrollment, cancellationToken);
                RequireOriginal();
                var user = await context.Users.IgnoreAutoIncludes().AsNoTracking()
                    .Where(user => user.Id == userId && !user.IsDeleted)
                    .Select(user => new User
                    {
                        Id = user.Id, IsDeleted = user.IsDeleted, LastActiveTenantId = user.LastActiveTenantId, Pii = null!
                    }).SingleOrDefaultAsync(cancellationToken);
                RequireOriginal();
                return user;
            }
            finally
            {
                if (connection is not null) connection.StateChange -= OnStateChange;
                if (relational) await context.Database.CloseConnectionAsync();
            }
        });
    }
}
