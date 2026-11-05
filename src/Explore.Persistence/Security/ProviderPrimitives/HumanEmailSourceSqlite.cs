using System.Data.Common;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Composes typed source facts on the dedicated email owner's original SQLite transaction.</summary>
internal static class HumanEmailSourceSqlite
{
    internal static async Task<HumanEmailSourceFacts> ReadAsync(ExploreDbContext context, DbTransaction? transaction,
        HumanCommitSubject subject, HumanAuthenticationLifetime lifetime, HumanSupportForwardingObservation forwarding,
        Guid? tenant, bool route, Action requireSelection, CancellationToken cancellationToken)
    {
        if (!context.Database.IsSqlite())
            throw new NotSupportedException("human_email_sqlite_owner_required");
        void RequireOriginal()
        {
            requireSelection();
            if (context.TenantFilterTenantId != tenant
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction()))
                throw new InvalidOperationException("human_email_original_transaction_changed");
        }
        RequireOriginal();
        if (transaction is not null)
        {
            await AcquireWriteReservationAsync(context, transaction, cancellationToken);
            RequireOriginal();
        }
        var principal = await HumanPrincipalSourceProjection.ReadAsync(context, context, subject,
            lifetime.LocalSession, RequireOriginal, cancellationToken);
        var support = await HumanSupportAccessSourceProjection.ReadAsync(context, tenant, forwarding,
            RequireOriginal, cancellationToken);
        var selectedRoute = route
            ? await HumanAuthorizationRouteProjection.ReadAsync(context, subject, tenant, RequireOriginal, cancellationToken)
            : null;
        RequireOriginal();
        return new(principal, support, selectedRoute);
    }

    /// <summary>Upgrades a borrowed deferred transaction without replacing its owner; EF exposes no upgrade API.</summary>
    private static async Task AcquireWriteReservationAsync(
        ExploreDbContext context, DbTransaction transaction, CancellationToken cancellationToken)
    {
        var entity = context.Model.FindEntityType(typeof(RolePermission))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var names = context.GetService<ISqlGenerationHelper>();
        string table = names.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        string role = names.DelimitIdentifier(entity.FindProperty(nameof(RolePermission.RoleId))!.GetColumnName(store)!);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        // Even with no matching rows SQLite takes its database-wide writer reservation until completion.
        command.CommandText = $"UPDATE {table} SET {role} = {role} WHERE 0";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
