using System.Data;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>
/// Fixed Global membership graph reads. Subject selection and membership status decisions belong to C#;
/// these shared credential permissions are not human authorization.
/// </summary>
public static class PostgresUserTenantMembershipReadPermissions
{
    private const string Policy = "membership_directory_global_read";
    private const string Drift = "membership_read_permissions_drift";
    private static readonly (Type Type, string[] Properties)[] Inventory =
    [
        (typeof(TenantUser), ["Id", "TenantId", "UserId", "ActorId", "StatusId", "JoinedAt", "SuspendedAt",
            "SuspendedBy", "BanExpiresAt", "RemovedAt", "RemovedBy", "ModerationNote", "CreatedAt", "CreatedBy",
            "UpdatedAt", "UpdatedBy", "IsDeleted", "DeletedAt", "DeletedBy"]),
        (typeof(Tenant), ["Id", "FullName", "Slug", "Description", "TenantStatusId", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"]),
        (typeof(TenantStatus), ["Id", "MasterCode", "FullName", "Description", "IsActiveState"])
    ];

    /// <summary>Pure source-owned descriptors for intersecting permission manifests; never database-derived authority.</summary>
    internal static bool TryGetGlobalReadColumns(ExploreDbContext context, Type type, out IReadOnlyList<string> columns)
    {
        var source = ReadSources(context).SingleOrDefault(source => source.Type == type);
        columns = source is null ? [] : Array.AsReadOnly(source.Columns.Select(column => column.Name).ToArray());
        return source is not null;
    }

    internal static IReadOnlyList<string> GlobalSourcePolicies(ExploreDbContext context, Type type, string login)
    {
        var source = ReadSources(context).SingleOrDefault(source => source.Type == type);
        return source?.TenantMetadata is null ? [] : [PolicyPredicate(source, login)];
    }

    internal static string GlobalPolicyException(ExploreDbContext context, string login)
    {
        var source = ReadSources(context).Single(source => source.Type == typeof(TenantUser));
        return $"(session_user={Text(login)} AND current_user=session_user AND n.nspname={Text(source.Schema)} AND c.relname={Text(source.Name)} AND ({PolicyPredicate(source, login)}))";
    }

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        var sources = ReadSources(context);
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
                throw new UnauthorizedAccessException("membership_installer_owner_changed");
        }
        connection.StateChange += Changed;
        string savepoint = "membership_permissions_" + Guid.CreateVersion7().ToString("N");
        try
        {
            RequireOriginal();
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                if (!await IsTrueAsync(context, Identity(enrollment.MigratorLogin), cancellationToken))
                    throw new UnauthorizedAccessException("membership_installer_wrong_identity");
                RequireOriginal();
                if (!await IsTrueAsync(context, NoObsoleteInstallation(sources), cancellationToken))
                    throw new InvalidOperationException("membership_requires_fresh_development_cluster");
                RequireOriginal();
                string complete = Manifest(context, sources, enrollment, complete: true);
                string fresh = Fresh(context, sources, enrollment);
                if (!await IsTrueAsync(context, $"({Manifest(context, sources, enrollment, complete: false)}) AND (({fresh}) OR ({complete}))", cancellationToken))
                    throw new InvalidOperationException(Drift);
                RequireOriginal();
                if (await IsTrueAsync(context, fresh, cancellationToken))
                {
                    RequireOriginal();
                    var names = context.GetService<ISqlGenerationHelper>();
                    foreach (var source in sources)
                    {
                        await context.Database.ExecuteSqlRawAsync($"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {names.DelimitIdentifier(enrollment.GlobalLogin)}; GRANT SELECT ({string.Join(',', source.Columns.Select(column => names.DelimitIdentifier(column.Name)))}) ON {source.Table} TO {names.DelimitIdentifier(enrollment.GlobalLogin)};", cancellationToken);
                        RequireOriginal();
                        if (source.TenantMetadata is not null)
                        {
                            await context.Database.ExecuteSqlRawAsync($"CREATE POLICY {names.DelimitIdentifier(Policy)} ON {source.Table} FOR SELECT TO {names.DelimitIdentifier(enrollment.GlobalLogin)} USING (true);", cancellationToken);
                            RequireOriginal();
                        }
                    }
                    if (!await IsTrueAsync(context, complete, cancellationToken))
                        throw new InvalidOperationException(Drift);
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

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException("membership_enrollment_required");
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
                throw new UnauthorizedAccessException("membership_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            // Reject role substitution using catalog-free identity SQL before protected relation lookup.
            if (!await IsTrueAsync(context, Identity(enrollment.GlobalLogin)
                + " AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL", cancellationToken))
                throw new UnauthorizedAccessException("membership_read_permissions_not_ready");
            RequireOriginal();
            var sources = ReadSources(context);
            if (!await IsTrueAsync(context, $"({NoObsoleteInstallation(sources)}) AND ({Manifest(context, sources, enrollment, complete: true)})", cancellationToken))
                throw new UnauthorizedAccessException("membership_read_permissions_not_ready");
            RequireOriginal();
        }
        finally { connection.StateChange -= Changed; }
    }

    private static string Manifest(ExploreDbContext context, Source[] sources, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string global = Text(enrollment.GlobalLogin), runtime = Text(enrollment.RuntimeLogin), migrator = Text(enrollment.MigratorLogin);
        string expected = "VALUES " + string.Join(',', sources.SelectMany(source =>
            Baseline(context, source).Concat(source.Columns.Select(column => new Grant(column.Name, "SELECT"))).Distinct()
                .Select(grant => $"({Text(source.Table)},{Text(grant.Column)},{Text(grant.Privilege)})")));
        string tables = "ARRAY[" + string.Join(',', sources.Select(source => Text(source.Table))) + "]::text[]";
        string required = complete ? string.Join(" AND ", sources.SelectMany(source => source.Columns.Select(column => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attname={Text(column.Name)}
                    AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={global})
                    AND acl.grantor=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={migrator})
                    AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
            """))) : "true";
        return $"""
            (WITH expected(relation,column_name,privilege) AS ({expected}), members AS
                (SELECT r.* FROM pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role({global},r.oid,'MEMBER'))
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={global} AND rolcanlogin)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={global}))
                AND NOT pg_catalog.pg_has_role({global},{runtime},'MEMBER')
                AND NOT pg_catalog.pg_has_role({runtime},{global},'MEMBER')
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={runtime} AND rolcanlogin)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                    WHERE pg_catalog.pg_has_role({runtime},r.oid,'MEMBER')
                        AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                            OR EXISTS (SELECT 1 FROM unnest(ARRAY[{string.Join(',', sources.Select(source => Text(source.Schema)).Distinct())}]::text[]) s(name)
                                WHERE pg_catalog.has_schema_privilege(r.oid,s.name,'CREATE'))
                            OR EXISTS (SELECT 1 FROM unnest({tables}) source(relation)
                                JOIN pg_catalog.pg_class t ON t.oid=pg_catalog.to_regclass(source.relation)
                                WHERE t.relowner=r.oid OR EXISTS (
                                    SELECT 1 FROM (VALUES ('SELECT'),('INSERT'),('UPDATE'),('DELETE'),('TRUNCATE'),('REFERENCES'),('TRIGGER'),('MAINTAIN')) permission(privilege)
                                    WHERE pg_catalog.has_table_privilege(r.oid,t.oid,permission.privilege || ' WITH GRANT OPTION'))
                                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                        CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                                        WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                                            AND pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')))))
                AND NOT EXISTS (SELECT 1 FROM members r WHERE r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                    OR EXISTS (SELECT 1 FROM unnest(ARRAY[{string.Join(',', sources.Select(source => Text(source.Schema)).Distinct())}]::text[]) s(name)
                        WHERE pg_catalog.has_schema_privilege(r.oid,s.name,'CREATE')))
                AND NOT EXISTS (SELECT 1 FROM unnest({tables}) source(relation)
                    JOIN pg_catalog.pg_class t ON t.oid=pg_catalog.to_regclass(source.relation) CROSS JOIN members r
                    WHERE r.oid=t.relowner OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                            WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                                AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                    OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege) AND NOT (
                                        a.attnum>0 AND r.rolname={global}
                                        AND EXISTS (SELECT 1 FROM expected e WHERE e.relation=source.relation AND e.column_name=a.attname AND e.privilege=permission.privilege)
                                        AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=r.oid
                                            AND acl.grantor=t.relowner AND acl.privilege_type=permission.privilege AND NOT acl.is_grantable)
                                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=0 AND acl.privilege_type=permission.privilege))))))
                AND ({required}) AND ({string.Join(" AND ", sources.Select(source => SourcePredicate(context, source, enrollment, complete)))}))
            """;
    }

    private static string SourcePredicate(ExploreDbContext context, Source source, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string columns = string.Join(" AND ", source.Columns.Select(column => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=c.oid AND a.attname={Text(column.Name)}
                AND a.attnum>0 AND NOT a.attisdropped AND a.attidentity='' AND a.attgenerated=''
                AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(column.StoreType)} AND a.attnotnull={(column.Required ? "true" : "false")})
            """));
        string policy = PolicyPredicate(source, enrollment.GlobalLogin);
        string baseline = source.TenantMetadata is null ? "NOT c.relrowsecurity AND NOT c.relforcerowsecurity" : $"""
            c.relrowsecurity AND c.relforcerowsecurity AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                WHERE p.polrelid=c.oid AND p.polname='tenant_isolation' AND p.polcmd='*' AND p.polpermissive
                    AND p.polroles=ARRAY[0::oid] AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                    AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(source.TenantMetadata, source.Schema))}
                        || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
            """;
        string[] sharedPolicies = [.. PostgresTenantCreationPermissions.GlobalSourcePolicies(context, source.Type, enrollment.GlobalLogin)];
        string allowedPolicies = string.Join(" OR ", sharedPolicies.Append(policy));
        string policyBoundary = source.TenantMetadata is null ? "true" : $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=c.oid
                AND (p.polname={Text(Policy)} OR (p.polname<>'tenant_isolation'
                    AND (0=ANY(p.polroles) OR (SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.GlobalLogin)})=ANY(p.polroles))))
                AND NOT ({allowedPolicies}))
            {(complete ? $"AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {policy})" : "")}
            """;
        return $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                WHERE c.oid=pg_catalog.to_regclass({Text(source.Table)}) AND c.relkind='r' AND NOT c.relispartition
                    AND c.relowner=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.MigratorLogin)})
                    AND n.nspname={Text(source.Schema)} AND ({columns}) AND ({baseline}) AND ({policyBoundary})
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid=c.oid OR i.inhparent=c.oid)
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class=c.oid))
            """;
    }

    private static string Fresh(ExploreDbContext context, Source[] sources, PostgresNativeQueueEnrollment enrollment) =>
        string.Join(" AND ", sources.Select(source =>
        {
            string baseline = string.Join(" OR ", Baseline(context, source).Select(grant =>
                $"(a.attname={Text(grant.Column)} AND acl.privilege_type={Text(grant.Privilege)})"));
            return $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                    WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)})
                        AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.GlobalLogin)})
                        AND NOT ({(baseline.Length == 0 ? "false" : baseline)}))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(Policy)})
                """;
        }));

    private static IEnumerable<Grant> Baseline(ExploreDbContext context, Source source)
    {
        PostgresTenantCreationPermissions.TryGetGlobalColumns(context, source.Type, out var read, out var insert);
        PostgresManagedTenantWorkerCreationPermissions.TryGetGlobalColumns(context, source.Type, out var workerRead, out var workerInsert, out var workerUpdate);
        return read.Concat(workerRead).Select(column => new Grant(column, "SELECT"))
            .Concat(insert.Concat(workerInsert).Select(column => new Grant(column, "INSERT")))
            .Concat(workerUpdate.Concat(PostgresTenantMembershipMutationPermissions.GlobalUpdateColumns(context, source.Type))
                .Select(column => new Grant(column, "UPDATE"))).Distinct();
    }

    private static string PolicyPredicate(Source source, string login) => $"""
        p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(Policy)}
        AND p.polcmd='r' AND p.polpermissive AND p.polroles=ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})]
        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)='true' AND p.polwithcheck IS NULL
        """;

    private static string NoObsoleteInstallation(Source[] sources) => $"""
        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN
            ('event_user_membership_owner','event_user_membership_global','event_user_membership_migrator'))
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
            WHERE n.nspname={Text(sources[0].Schema)} AND p.proname={Text(sources[0].Name + "_active_for_user")})
        """;

    private static string Identity(string login) => $"session_user={Text(login)} AND current_user=session_user AND pg_catalog.current_setting('role') IN ('none',session_user::text)";
    private static async Task<bool> IsTrueAsync(ExploreDbContext context, string predicate, CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT ({predicate})";
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static Source[] ReadSources(ExploreDbContext context)
    {
        var names = context.GetService<ISqlGenerationHelper>();
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        return Inventory.Select(item =>
        {
            var entity = context.Model.FindEntityType(item.Type) ?? throw new InvalidOperationException("membership_model_missing");
            string table = entity.GetTableName() ?? throw new InvalidOperationException("membership_table_missing");
            string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema ?? entity.GetSchema() ?? "public";
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
            if (entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.GetComplexProperties().Any()
                || entity.FindPrimaryKey()?.Properties is not [var key] || key.Name != "Id")
                throw new InvalidOperationException("membership_model_shape_drift");
            var columns = item.Properties.Select(name =>
            {
                var property = entity.FindProperty(name) ?? throw new InvalidOperationException("membership_property_missing");
                if (property.IsShadowProperty()) throw new InvalidOperationException("membership_property_shape_drift");
                return new Column(property.GetColumnName(store)!, property.GetRelationalTypeMapping().StoreType, !property.IsNullable);
            }).ToArray();
            var metadata = tenantTables.SingleOrDefault(value => value.TableName == table && (value.Schema ?? schema) == schema);
            if (item.Type == typeof(TenantUser) ? metadata is null || metadata.AllowsNullTenant || metadata.ParentJoin is not null : metadata is not null)
                throw new InvalidOperationException("membership_tenant_shape_drift");
            return new Source(item.Type, schema, table, names.DelimitIdentifier(table, schema), columns, metadata);
        }).ToArray();
    }

    private sealed record Column(string Name, string StoreType, bool Required);
    private sealed record Grant(string Column, string Privilege);
    private sealed record Source(Type Type, string Schema, string Name, string Table, Column[] Columns,
        TenantTableMetadata? TenantMetadata);
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
