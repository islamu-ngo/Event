using Explore.Domain;
using Explore.Domain.Federation;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Enumerates the Global login's settlement permissions. Private rows retain
/// immutable-tenant RLS; cross-tenant discovery uses the disclosure executor only.
/// </summary>
public static class PostgresPdsSettlementContract
{
    public const string GlobalRole = "event_pds_settlement_global";

    /// <summary>Install with migration authority after the disclosure contract.</summary>
    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(Schema(context));
        var grants = Permissions(context);
        string permissions = string.Join(Environment.NewLine, grants.Select(permission =>
            $"REVOKE ALL ON TABLE {permission.Table} FROM {GlobalRole};\n" +
            $"GRANT {permission.Privilege} ON TABLE {permission.Table} TO {GlobalRole};"));
        return $"""
            DO $settlement$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}') THEN
                    CREATE ROLE {GlobalRole} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE
                        NOINHERIT NOREPLICATION NOBYPASSRLS;
                END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}'
                    AND (rolcanlogin OR rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls))
                    OR pg_has_role('{GlobalRole}', '{PostgresDiscoveryDisclosureContract.OwnerRole}', 'MEMBER')
                    OR pg_has_role('{GlobalRole}', '{PostgresDiscoveryDisclosureContract.MigratorRole}', 'MEMBER') THEN
                    RAISE EXCEPTION 'pds_settlement_authority_not_ready';
                END IF;
            END; $settlement$;
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            GRANT {PostgresDiscoveryDisclosureContract.RuntimeRole} TO {GlobalRole}
                WITH INHERIT TRUE, SET FALSE;
            {permissions}
            """;
    }

    internal static async Task RequireReadyAsync(ExploreDbContext context, CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        var permissions = Permissions(context);
        string[] functions =
        [
            PostgresDiscoveryDisclosureContract.FunctionSql(context, "catalog") + "()",
            PostgresDiscoveryDisclosureContract.FunctionSql(context, "enrollment") + "()",
            PostgresDiscoveryDisclosureContract.FunctionSql(context, "dependencies") + "(text,uuid[])",
            PostgresDiscoveryDisclosureContract.FunctionSql(context, "fence_tenants") + "(uuid[])",
            PostgresDiscoveryDisclosureContract.FunctionSql(context, "finalize") + "(uuid[],uuid[],boolean[],bigint[],bigint[])"
        ];
        var checks = new List<string> { $"pg_has_role(current_user, '{GlobalRole}', 'USAGE')" };
        foreach (var permission in permissions)
        {
            foreach (string privilege in permission.Checks)
                checks.Add(privilege);
        }
        checks.AddRange(functions.Select(function =>
            $"has_function_privilege(current_user, '{function}', 'EXECUTE')"));
        command.CommandText = "SELECT " + string.Join(" AND ", checks);
        command.CommandTimeout = 5;
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException("pds_settlement_authority_not_ready");
    }

    private static Permission[] Permissions(ExploreDbContext context)
    {
        return
        [
            For<Tenant>(false, [nameof(Tenant.Id)], [nameof(Tenant.Id)]),
            For<User>(false, [nameof(User.Id)], [nameof(User.Id)]),
            For<Actor>(false, [nameof(Actor.Id), nameof(Actor.UserId), nameof(Actor.OrganizationId), nameof(Actor.GroupId)],
                [nameof(Actor.Id)]),
            For<AtprotoRecord>(true, null,
                [nameof(AtprotoRecord.Id), nameof(AtprotoRecord.Uri), nameof(AtprotoRecord.Cid),
                 nameof(AtprotoRecord.RecordJson), nameof(AtprotoRecord.RecordHash), nameof(AtprotoRecord.IndexedAt),
                 nameof(AtprotoRecord.UpdatedAt), nameof(AtprotoRecord.TombstonedAt),
                 nameof(AtprotoRecord.Direction), nameof(AtprotoRecord.Provenance)]),
            For<PdsSyncOutbox>(false, null,
                [nameof(PdsSyncOutbox.AtprotoRecordId), nameof(PdsSyncOutbox.Status), nameof(PdsSyncOutbox.ProcessedAt),
                 nameof(PdsSyncOutbox.LastError), nameof(PdsSyncOutbox.LeaseOwner), nameof(PdsSyncOutbox.LeaseToken),
                 nameof(PdsSyncOutbox.LeaseExpiresAt), nameof(PdsSyncOutbox.SettledUri), nameof(PdsSyncOutbox.SettledCid)]),
            For<AtprotoOutboundRecordOwnership>(true, null,
                [nameof(AtprotoOutboundRecordOwnership.SourceVersion), nameof(AtprotoOutboundRecordOwnership.UpdatedAt)]),
            For<AtprotoRecordTenantPresentation>(true, null,
                [nameof(AtprotoRecordTenantPresentation.IsVisible), nameof(AtprotoRecordTenantPresentation.SourceVersion),
                 nameof(AtprotoRecordTenantPresentation.EvaluatedAt)]),
            For<Event>(false, null,
                [nameof(Event.Id), nameof(Event.AtprotoRecordId), nameof(Event.ConcurrencyStamp), nameof(Event.UpdatedAt)]),
            For<EventDiscoveryRevision>(true, null,
                [nameof(EventDiscoveryRevision.DisclosureEpoch), nameof(EventDiscoveryRevision.IdentityEpoch)])
        ];

        Permission For<T>(bool insert, string[]? read, string[] update)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var sql = context.GetService<ISqlGenerationHelper>();
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            string table = sql.DelimitIdentifier(entity.GetTableName()!, Schema(context));
            string Column(string property) => entity.FindProperty(property)!.GetColumnName(store)!;
            string[] updates = update.Select(Column).ToArray();
            string[]? reads = read?.Select(Column).ToArray();
            string select = reads is null ? "SELECT" : $"SELECT ({string.Join(", ", reads.Select(sql.DelimitIdentifier))})";
            string privilege = select + (insert ? ", INSERT" : "") +
                $", UPDATE ({string.Join(", ", updates.Select(sql.DelimitIdentifier))})";
            var checks = new List<string>();
            if (reads is null)
                checks.Add($"has_table_privilege(current_user, '{table}', 'SELECT')");
            else
                checks.AddRange(reads.Select(column => $"has_column_privilege(current_user, '{table}', '{column}', 'SELECT')"));
            if (insert)
                checks.Add($"has_table_privilege(current_user, '{table}', 'INSERT')");
            checks.AddRange(updates.Select(column => $"has_column_privilege(current_user, '{table}', '{column}', 'UPDATE')"));
            return new(table, privilege, checks.ToArray());
        }
    }

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(Tenant))!.GetSchema() ?? "public";

    private sealed record Permission(string Table, string Privilege, string[] Checks);
}
