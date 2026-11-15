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
/// Fixed primitive Global reads for administrative scope enumeration.
/// Subject, role, lifecycle and tenant decisions belong to the application queries.
/// </summary>
public static class PostgresAdministrativeAuthorityReadPermissions
{
    private const string Policy = "administrative_scope_global_read";
    private const string Drift = "administrative_scope_read_permissions_drift";
    private static readonly (Type Type, string[] Properties, bool Tenant)[] Inventory =
    [
        (typeof(TenantUserRoleGrant), ["TenantId", "TenantUserId", "RoleId", "RevokedAt"], true),
        (typeof(OrganizationMember), ["UserId", "TenantId", "RoleId", "OrganizationTenantId", "IsDeleted"], true),
        (typeof(OrganizationTenant), ["Id", "TenantId", "OrganizationId", "IsDeleted"], true),
        (typeof(Organization), ["Id", "IsDeleted"], false),
        (typeof(GroupMember), ["UserId", "TenantId", "RoleId", "GroupTenantId", "IsDeleted"], true),
        (typeof(GroupTenant), ["Id", "TenantId", "GroupId", "IsDeleted"], true),
        (typeof(Group), ["Id", "IsDeleted"], false)
    ];

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

    internal static string GlobalPolicyException(ExploreDbContext context, string login) =>
        "(" + string.Join(" OR ", ReadSources(context).Where(source => source.TenantMetadata is not null)
            .Select(source => $"(session_user={Text(login)} AND current_user=session_user AND n.nspname={Text(source.Schema)} AND c.relname={Text(source.Name)} AND ({PolicyPredicate(source, login)}))")) + ")";

    internal static IReadOnlyList<string> GlobalCatalogueRelations(ExploreDbContext context) =>
        ReadSources(context).Select(source => source.Table).ToArray();

    internal static string CompleteGlobalCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        $"({NoObsoleteInstallation(context)}) AND ({Manifest(context, ReadSources(context), enrollment, true)})";

    internal static string OptionalGlobalCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sources = ReadSources(context);
        return $"({NoObsoleteInstallation(context)}) AND ({Manifest(context, sources, enrollment, false)}) AND (({Fresh(context, sources, enrollment)}) OR ({Manifest(context, sources, enrollment, true)}))";
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
                throw new UnauthorizedAccessException("administrative_scope_installer_owner_changed");
        }
        connection.StateChange += Changed;
        string savepoint = "administrative_scope_" + Guid.CreateVersion7().ToString("N");
        try
        {
            RequireOriginal();
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                if (!await IsTrueAsync(context, Identity(enrollment.MigratorLogin), cancellationToken))
                    throw new UnauthorizedAccessException("administrative_scope_installer_wrong_identity");
                RequireOriginal();
                if (!await IsTrueAsync(context, NoObsoleteInstallation(context), cancellationToken))
                    throw new InvalidOperationException("administrative_scope_requires_fresh_development_cluster");
                RequireOriginal();
                string complete = Manifest(context, sources, enrollment, true);
                string fresh = Fresh(context, sources, enrollment);
                if (!await IsTrueAsync(context,
                    $"({Manifest(context, sources, enrollment, false)}) AND (({fresh}) OR ({complete}))", cancellationToken))
                    throw new InvalidOperationException(Drift);
                RequireOriginal();
                if (await IsTrueAsync(context, fresh, cancellationToken))
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
                                $"CREATE POLICY {names.DelimitIdentifier(Policy)} ON {source.Table} FOR SELECT TO {names.DelimitIdentifier(enrollment.GlobalLogin)} USING (true);";
                            await command.ExecuteNonQueryAsync(cancellationToken);
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
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={runtime} AND rolcanlogin)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={global}))
                AND NOT pg_catalog.pg_has_role({global},{runtime},'MEMBER')
                AND NOT pg_catalog.pg_has_role({runtime},{global},'MEMBER')
                AND NOT EXISTS (SELECT 1 FROM members r
                    WHERE r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                        OR EXISTS (SELECT 1 FROM unnest(ARRAY[{string.Join(',', sources.Select(source => Text(source.Schema)).Distinct())}]::text[]) s(name)
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
                AND ({string.Join(" AND ", sources.Select(source => SourcePredicate(context, source, enrollment, complete)))}))
            """;
    }

    private static string SourcePredicate(ExploreDbContext context, Source source, PostgresNativeQueueEnrollment enrollment, bool complete)
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
        string policy = PolicyPredicate(source, enrollment.GlobalLogin);
        string allowed = string.Join(" OR ", PostgresTenantCreationPermissions.GlobalSourcePolicies(context, source.Type, enrollment.GlobalLogin)
            .Append(policy));
        string boundary = source.TenantMetadata is null ? "true" : $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=c.oid
                AND (p.polname={Text(Policy)} OR (p.polname<>'tenant_isolation'
                    AND (0=ANY(p.polroles) OR (SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.GlobalLogin)})=ANY(p.polroles))))
                AND NOT ({allowed}))
            {(complete ? $"AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {policy})" : "")}
            """;
        return $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                WHERE c.oid=pg_catalog.to_regclass({Text(source.Table)}) AND c.relkind='r' AND c.relpersistence='p'
                    AND c.relowner=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.MigratorLogin)})
                    AND n.nspname={Text(source.Schema)} AND ({columns}) AND ({tenant}) AND ({boundary})
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
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(Policy)})
                """;
        }));

    private static IEnumerable<Grant> Baseline(ExploreDbContext context, Source source)
    {
        PostgresTenantCreationPermissions.TryGetGlobalColumns(context, source.Type, out var read, out var insert);
        PostgresManagedTenantWorkerCreationPermissions.TryGetGlobalColumns(context, source.Type, out var workerRead, out var workerInsert, out var workerUpdate);
        return read.Concat(workerRead).Select(column => new Grant(column, "SELECT"))
            .Concat(insert.Concat(workerInsert).Select(column => new Grant(column, "INSERT")))
            .Concat(workerUpdate.Concat(PostgresTenantMembershipMutationPermissions.GlobalUpdateColumns(context, source.Type))
                .Select(column => new Grant(column, "UPDATE")))
            .Where(grant => grant.Privilege != "SELECT" || !source.Columns.Any(column => column.Name == grant.Column)).Distinct();
    }

    private static string PolicyPredicate(Source source, string login) => $"""
        p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(Policy)}
        AND p.polcmd='r' AND p.polpermissive AND p.polroles=ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})]
        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)='true' AND p.polwithcheck IS NULL
        """;

    private static string NoObsoleteInstallation(ExploreDbContext context)
    {
        var names = new HumanPrincipalSourceSql(context);
        (Type Type, string Suffix)[] retired =
        [
            (typeof(PlatformUserRole), "_is_admin_for_user"), (typeof(TenantUserRoleGrant), "_admin_tenants_for_user"),
            (typeof(OrganizationMember), "_admin_organizations_for_user"), (typeof(GroupMember), "_admin_groups_for_user")
        ];
        return $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                WHERE rolname IN ('event_admin_authority_owner','event_admin_authority_migrator','event_admin_authority_global'))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
                WHERE n.nspname={Text(names.Schema)} AND p.proname IN
                    ({string.Join(',', retired.Select(item => Text(context.Model.FindEntityType(item.Type)!.GetTableName()! + item.Suffix)))}))
            """;
    }

    private static Source[] ReadSources(ExploreDbContext context)
    {
        var names = context.GetService<ISqlGenerationHelper>();
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        return Inventory.Select(item =>
        {
            var entity = context.Model.FindEntityType(item.Type) ?? throw new InvalidOperationException("administrative_scope_model_missing");
            string table = entity.GetTableName() ?? throw new InvalidOperationException("administrative_scope_table_missing");
            string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
                ?? entity.GetSchema() ?? "public";
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
            if (entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.GetComplexProperties().Any()
                || entity.FindPrimaryKey()?.Properties is not [var key] || key.Name != "Id")
                throw new InvalidOperationException("administrative_scope_model_shape_drift");
            var columns = item.Properties.Select(name =>
            {
                var property = entity.FindProperty(name) ?? throw new InvalidOperationException("administrative_scope_property_missing");
                if (property.IsShadowProperty()) throw new InvalidOperationException("administrative_scope_property_shape_drift");
                return new Column(property.GetColumnName(store)!, property.GetRelationalTypeMapping().StoreType, !property.IsNullable);
            }).ToArray();
            var metadata = tenantTables.SingleOrDefault(value => value.TableName == table && (value.Schema ?? schema) == schema);
            if (item.Tenant ? metadata is null || metadata.AllowsNullTenant || metadata.ParentJoin is not null : metadata is not null)
                throw new InvalidOperationException("administrative_scope_tenant_shape_drift");
            return new Source(item.Type, schema, table, names.DelimitIdentifier(table, schema), columns, metadata);
        }).ToArray();
    }

    private static string Identity(string login) =>
        $"session_user={Text(login)} AND current_user=session_user AND pg_catalog.current_setting('role') IN ('none',session_user::text)";

    private static async Task<bool> IsTrueAsync(ExploreDbContext context, string predicate, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT ({predicate})";
        return await command.ExecuteScalarAsync(token) is true;
    }

    private sealed record Column(string Name, string StoreType, bool Required);
    private sealed record Grant(string Column, string Privilege);
    private sealed record Source(Type Type, string Schema, string Name, string Table, Column[] Columns,
        TenantTableMetadata? TenantMetadata);
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
