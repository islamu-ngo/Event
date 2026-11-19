using System.Data;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>
/// Shared metadata, SQL manifest, and physical-custody installer mechanics for Global read-permission contracts.
/// Each owner declares its own column inventory and obsolescence predicate without delegating authorization authority.
/// </summary>
internal static class PostgresGlobalReadPermissionsHelper
{
    internal sealed record ColumnDescriptor(string Name, string StoreType, bool Required);
    internal sealed record GrantDescriptor(string Column, string Privilege);
    internal sealed record SourceDescriptor(Type Type, string Schema, string Name, string Table,
        ColumnDescriptor[] Columns, TenantTableMetadata? TenantMetadata);

    internal static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";

    internal static string IdentitySql(string login) =>
        $"session_user={Text(login)} AND current_user=session_user AND pg_catalog.current_setting('role') IN ('none',session_user::text)";

    internal static async Task<bool> IsTrueAsync(ExploreDbContext context, string predicate, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT ({predicate})";
        return await command.ExecuteScalarAsync(token) is true;
    }

    internal static bool TryGetGlobalReadColumns(SourceDescriptor[] sources, Type type, out IReadOnlyList<string> columns)
    {
        var source = sources.SingleOrDefault(item => item.Type == type);
        columns = source is null ? [] : Array.AsReadOnly(source.Columns.Select(column => column.Name).ToArray());
        return source is not null;
    }

    internal static IReadOnlyList<string> GlobalSourcePolicies(SourceDescriptor[] sources, Type type, string policyName, string login)
    {
        var source = sources.SingleOrDefault(item => item.Type == type);
        return source?.TenantMetadata is null ? [] : [BuildPolicyPredicateSql(source, policyName, login)];
    }

    internal static string BuildPolicyPredicateSql(SourceDescriptor source, string policyName, string login) => $"""
        p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(policyName)}
        AND p.polcmd='r' AND p.polpermissive AND p.polroles=ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})]
        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)='true' AND p.polwithcheck IS NULL
        """;

    internal static SourceDescriptor[] ReadSources(ExploreDbContext context,
        IEnumerable<(Type Type, string[] Properties)> inventory, Func<Type, TenantTableMetadata?, bool> validateTenantShape,
        string errorPrefix)
    {
        var names = context.GetService<ISqlGenerationHelper>();
        var tenantTables = context.GetTenantTableInventory();
        return inventory.Select(item =>
        {
            var entity = context.Model.FindEntityType(item.Type) ?? throw new InvalidOperationException($"{errorPrefix}_model_missing");
            string table = entity.GetTableName() ?? throw new InvalidOperationException($"{errorPrefix}_table_missing");
            string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
                ?? entity.GetSchema() ?? "public";
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
            if (entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.GetComplexProperties().Any()
                || entity.FindPrimaryKey()?.Properties is not [var key] || key.Name != "Id")
                throw new InvalidOperationException($"{errorPrefix}_model_shape_drift");
            var columns = item.Properties.Select(name =>
            {
                var property = entity.FindProperty(name) ?? throw new InvalidOperationException($"{errorPrefix}_property_missing");
                if (property.IsShadowProperty()) throw new InvalidOperationException($"{errorPrefix}_property_shape_drift");
                return new ColumnDescriptor(property.GetColumnName(store)!, property.GetRelationalTypeMapping().StoreType, !property.IsNullable);
            }).ToArray();
            var metadata = tenantTables.SingleOrDefault(value => value.TableName == table && (value.Schema ?? schema) == schema);
            if (!validateTenantShape(item.Type, metadata))
                throw new InvalidOperationException($"{errorPrefix}_tenant_shape_drift");
            return new SourceDescriptor(item.Type, schema, table, names.DelimitIdentifier(table, schema), columns, metadata);
        }).ToArray();
    }

    internal static IEnumerable<GrantDescriptor> BuildBaselineGrants(ExploreDbContext context, SourceDescriptor source,
        bool excludeOwnSelectFromBaseline = true)
    {
        PostgresTenantCreationPermissions.TryGetGlobalColumns(context, source.Type, out var read, out var insert);
        PostgresManagedTenantWorkerCreationPermissions.TryGetGlobalColumns(context, source.Type, out var workerRead, out var workerInsert, out var workerUpdate);
        return read.Concat(workerRead).Select(column => new GrantDescriptor(column, "SELECT"))
            .Concat(insert.Concat(workerInsert).Select(column => new GrantDescriptor(column, "INSERT")))
            .Concat(workerUpdate.Concat(PostgresTenantMembershipMutationPermissions.GlobalUpdateColumns(context, source.Type))
                .Select(column => new GrantDescriptor(column, "UPDATE")))
            .Where(grant => !excludeOwnSelectFromBaseline || grant.Privilege != "SELECT"
                || !source.Columns.Any(column => column.Name == grant.Column))
            .Distinct();
    }

    internal static string BuildManifestSql(ExploreDbContext context, SourceDescriptor[] sources,
        PostgresNativeQueueEnrollment enrollment, string policyName, bool complete, bool includeRuntimePrivilegeAudit,
        bool checkNotPartition)
    {
        string global = Text(enrollment.GlobalLogin), runtime = Text(enrollment.RuntimeLogin), migrator = Text(enrollment.MigratorLogin);
        string expected = "VALUES " + string.Join(',', sources.SelectMany(source =>
            BuildBaselineGrants(context, source).Concat(source.Columns.Select(column => new GrantDescriptor(column.Name, "SELECT"))).Distinct()
                .Select(grant => $"({Text(source.Table)},{Text(grant.Column)},{Text(grant.Privilege)})")));
        string tables = "ARRAY[" + string.Join(',', sources.Select(source => Text(source.Table))) + "]::text[]";
        string required = complete ? string.Join(" AND ", sources.SelectMany(source => source.Columns.Select(column => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attname={Text(column.Name)}
                    AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={global})
                    AND acl.grantor=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={migrator})
                    AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
            """))) : "true";
        string schemas = string.Join(',', sources.Select(source => Text(source.Schema)).Distinct());
        string runtimeAudit = includeRuntimePrivilegeAudit ? $"""
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                    WHERE pg_catalog.pg_has_role({runtime},r.oid,'MEMBER')
                        AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                            OR EXISTS (SELECT 1 FROM unnest(ARRAY[{schemas}]::text[]) s(name)
                                WHERE pg_catalog.has_schema_privilege(r.oid,s.name,'CREATE'))
                            OR EXISTS (SELECT 1 FROM unnest({tables}) source(relation)
                                JOIN pg_catalog.pg_class t ON t.oid=pg_catalog.to_regclass(source.relation)
                                WHERE t.relowner=r.oid OR EXISTS (
                                    SELECT 1 FROM (VALUES ('SELECT'),('INSERT'),('UPDATE'),('DELETE'),('TRUNCATE'),('REFERENCES'),('TRIGGER'),('MAINTAIN')) permission(privilege)
                                    WHERE (CASE WHEN permission.privilege='MAINTAIN'
                                        THEN {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", true)}
                                        ELSE pg_catalog.has_table_privilege(r.oid,t.oid,permission.privilege || ' WITH GRANT OPTION') END))
                                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                        CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                                        WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                                            AND pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')))))
            """ : string.Empty;
        string sourcePredicates = string.Join(" AND ", sources.Select(source =>
            BuildSourcePredicateSql(context, source, enrollment, policyName, complete, checkNotPartition)));
        return $"""
            (WITH expected(relation,column_name,privilege) AS ({expected}), members AS
                (SELECT r.* FROM pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role({global},r.oid,'MEMBER'))
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={global} AND rolcanlogin)
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={runtime} AND rolcanlogin)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={global}))
                AND NOT pg_catalog.pg_has_role({global},{runtime},'MEMBER')
                AND NOT pg_catalog.pg_has_role({runtime},{global},'MEMBER'){runtimeAudit}
                AND NOT EXISTS (SELECT 1 FROM members r
                    WHERE r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                        OR EXISTS (SELECT 1 FROM unnest(ARRAY[{schemas}]::text[]) s(name)
                            WHERE pg_catalog.has_schema_privilege(r.oid,s.name,'CREATE')))
                AND NOT EXISTS (SELECT 1 FROM unnest({tables}) source(relation)
                    JOIN pg_catalog.pg_class t ON t.oid=pg_catalog.to_regclass(source.relation) CROSS JOIN members r
                    WHERE r.oid=t.relowner
                        OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", false)}
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                            WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                                AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                    OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege) AND NOT (
                                        a.attnum>0 AND r.rolname={global}
                                        AND EXISTS (SELECT 1 FROM expected e
                                            WHERE e.relation=source.relation AND e.column_name=a.attname AND e.privilege=permission.privilege)
                                        AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                            WHERE acl.grantee=r.oid AND acl.grantor=t.relowner
                                                AND acl.privilege_type=permission.privilege AND NOT acl.is_grantable)
                                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                            WHERE acl.grantee=0 AND acl.privilege_type=permission.privilege))))))
                AND ({required})
                AND ({sourcePredicates}))
            """;
    }

    internal static string BuildSourcePredicateSql(ExploreDbContext context, SourceDescriptor source,
        PostgresNativeQueueEnrollment enrollment, string policyName, bool complete, bool checkNotPartition)
    {
        string columns = string.Join(" AND ", source.Columns.Select(column => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=c.oid AND a.attname={Text(column.Name)}
                AND a.attnum>0 AND NOT a.attisdropped AND a.attidentity='' AND a.attgenerated=''
                AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(column.StoreType)}
                AND a.attnotnull={(column.Required ? "true" : "false")})
            """));
        string tenant = source.TenantMetadata is null ? "NOT c.relrowsecurity AND NOT c.relforcerowsecurity" : $"""
            c.relrowsecurity AND c.relforcerowsecurity AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                WHERE p.polrelid=c.oid AND p.polname='tenant_isolation' AND p.polcmd='*' AND p.polpermissive
                    AND p.polroles=ARRAY[0::oid] AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                    AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(source.TenantMetadata, source.Schema))}
                        || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
            """;
        string policy = BuildPolicyPredicateSql(source, policyName, enrollment.GlobalLogin);
        string allowed = string.Join(" OR ", PostgresTenantCreationPermissions.GlobalSourcePolicies(context, source.Type, enrollment.GlobalLogin)
            .Append(policy));
        string boundary = source.TenantMetadata is null ? "true" : $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=c.oid
                AND (p.polname={Text(policyName)} OR (p.polname<>'tenant_isolation'
                    AND (0=ANY(p.polroles) OR (SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.GlobalLogin)})=ANY(p.polroles))))
                AND NOT ({allowed}))
            {(complete ? $"AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {policy})" : "")}
            """;
        string persistenceCheck = checkNotPartition ? "NOT c.relispartition" : "c.relpersistence='p'";
        return $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                WHERE c.oid=pg_catalog.to_regclass({Text(source.Table)}) AND c.relkind='r' AND {persistenceCheck}
                    AND c.relowner=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.MigratorLogin)})
                    AND n.nspname={Text(source.Schema)} AND ({columns}) AND ({tenant}) AND ({boundary})
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid=c.oid OR i.inhparent=c.oid)
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class=c.oid))
            """;
    }

    internal static string BuildFreshPredicateSql(ExploreDbContext context, SourceDescriptor[] sources,
        PostgresNativeQueueEnrollment enrollment, string policyName, bool excludeOwnSelectFromBaseline = true) =>
        string.Join(" AND ", sources.Select(source =>
        {
            string baseline = string.Join(" OR ", BuildBaselineGrants(context, source, excludeOwnSelectFromBaseline).Select(grant =>
                $"(a.attname={Text(grant.Column)} AND acl.privilege_type={Text(grant.Privilege)})"));
            return $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                    WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)})
                        AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.GlobalLogin)})
                        AND NOT ({(baseline.Length == 0 ? "false" : baseline)}))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(policyName)})
                """;
        }));

    internal static async Task ApplyWithSavepointAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        SourceDescriptor[] sources, string policyName, string savepointPrefix, string installerPrefix, string driftError,
        string noObsoleteSql, Func<bool, string> manifestSql, string freshSql, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
        var connection = RelationalConnectionIdentity.Capture(context);
        var physical = transaction.GetDbTransaction();
        var contextId = context.ContextId;
        int? backend = connection.BackendProcessId;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.ContextId != contextId || interrupted || !connection.Matches(context)
                || connection.State != ConnectionState.Open || connection.BackendProcessId != backend
                || !ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction())
                || !connection.Owns(physical))
                throw new UnauthorizedAccessException($"{installerPrefix}_installer_owner_changed");
        }
        connection.StateChange += Changed;
        string savepoint = savepointPrefix + "_" + Guid.CreateVersion7().ToString("N");
        try
        {
            RequireOriginal();
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                if (!await IsTrueAsync(context, IdentitySql(enrollment.MigratorLogin), cancellationToken))
                    throw new UnauthorizedAccessException($"{installerPrefix}_installer_wrong_identity");
                RequireOriginal();
                if (!await IsTrueAsync(context, noObsoleteSql, cancellationToken))
                    throw new InvalidOperationException($"{installerPrefix}_requires_fresh_development_cluster");
                RequireOriginal();
                string complete = manifestSql(true);
                if (!await IsTrueAsync(context, $"({manifestSql(false)}) AND (({freshSql}) OR ({complete}))", cancellationToken))
                    throw new InvalidOperationException(driftError);
                RequireOriginal();
                if (await IsTrueAsync(context, freshSql, cancellationToken))
                {
                    var names = context.GetService<ISqlGenerationHelper>();
                    foreach (var source in sources)
                    {
                        RequireOriginal();
                        // DDL identifiers require provider quoting rather than value parameters.
                        await using var command = context.Database.GetDbConnection().CreateCommand();
                        command.Transaction = physical;
                        command.CommandText =
                            $"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {names.DelimitIdentifier(enrollment.GlobalLogin)}; GRANT SELECT ({string.Join(',', source.Columns.Select(column => names.DelimitIdentifier(column.Name)))}) ON {source.Table} TO {names.DelimitIdentifier(enrollment.GlobalLogin)};";
                        await command.ExecuteNonQueryAsync(cancellationToken);
                        RequireOriginal();
                        if (source.TenantMetadata is not null)
                        {
                            command.CommandText =
                                $"CREATE POLICY {names.DelimitIdentifier(policyName)} ON {source.Table} FOR SELECT TO {names.DelimitIdentifier(enrollment.GlobalLogin)} USING (true);";
                            await command.ExecuteNonQueryAsync(cancellationToken);
                            RequireOriginal();
                        }
                    }
                    if (!await IsTrueAsync(context, complete, cancellationToken))
                        throw new InvalidOperationException(driftError);
                }
                RequireOriginal();
            }
            catch
            {
                await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                throw;
            }
            finally { await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None); }
            RequireOriginal();
            if (owned is not null) await owned.CommitAsync(cancellationToken);
        }
        finally { connection.StateChange -= Changed; }
    }

    internal static async Task RequireReadyWithCustodyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        string enrollmentRequiredError, string ownerChangedError, string notReadyError,
        Func<string> noObsoleteSql, Func<string> completeManifestSql, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException(enrollmentRequiredError);
        enrollment.Validate();
        context.RequireBoundTenantLease();
        var connection = RelationalConnectionIdentity.Capture(context);
        var physical = context.Database.CurrentTransaction?.GetDbTransaction();
        var contextId = context.ContextId;
        int? backend = connection.BackendProcessId;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (context.ContextId != contextId || context.TenantFilterTenantId is not null
                || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
                || interrupted || !connection.Matches(context) || connection.State != ConnectionState.Open
                || connection.BackendProcessId != backend
                || !ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction())
                || physical is not null && !connection.Owns(physical))
                throw new UnauthorizedAccessException(ownerChangedError);
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            // Reject role substitution using catalog-free identity SQL before protected relation lookup.
            if (!await IsTrueAsync(context, IdentitySql(enrollment.GlobalLogin)
                + " AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL", cancellationToken))
                throw new UnauthorizedAccessException(notReadyError);
            RequireOriginal();
            if (!await IsTrueAsync(context, $"({noObsoleteSql()}) AND ({completeManifestSql()})", cancellationToken))
                throw new UnauthorizedAccessException(notReadyError);
            RequireOriginal();
        }
        finally { connection.StateChange -= Changed; }
    }
}
