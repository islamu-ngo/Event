using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.References;
using Explore.Persistence.Configurations.Entities;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Explore.Persistence.Schema;

/// <summary>
/// Storage admission for the fixed worker manifest, separate from original worker
/// claim and graph authority. Only the declared Global account receives the literal
/// manifest; no live privilege contributes to the allowed set.
/// </summary>
public static class PostgresManagedTenantWorkerCreationPermissions
{
    private const string NotReady = "managed_tenant_worker_creation_permissions_not_ready";
    private const string Drift = "managed_tenant_worker_creation_permissions_drift";
    private const string ReadPolicy = "managed_tenant_worker_read";
    private const string InsertPolicy = "managed_tenant_worker_insert";
    private const string InsertRestriction = "managed_tenant_worker_insert_session";

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        IdentityDatabaseTopology identityTopology, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        if (!Enum.IsDefined(identityTopology)) throw new ArgumentOutOfRangeException(nameof(identityTopology));
        enrollment.Validate();
        var sources = Sources(context);
        string absent = AbsentPredicate(sources, enrollment);
        string complete = CataloguePredicate(context, sources, enrollment, true);
        var mechanics = ProvisioningSharedWriterAssertions.ManagedTenantWorkerCreationInventory(context, enrollment, identityTopology);
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
        string savepoint = "worker_creation_permissions_" + Guid.CreateVersion7().ToString("N");
        await transaction.CreateSavepointAsync(savepoint, cancellationToken);
        try
        {
            if (await ScalarAsync(context, $"SELECT session_user={Text(enrollment.MigratorLogin)} AND current_user=session_user AND pg_catalog.current_setting('role') IN ('none',session_user::text)", cancellationToken) is not true)
                throw new UnauthorizedAccessException("managed_tenant_worker_installer_wrong_identity");
            if (await ScalarAsync(context, $"SELECT ({CataloguePredicate(context, sources, enrollment, false)}) AND (({absent}) OR ({complete}))", cancellationToken) is not true)
                throw new InvalidOperationException(Drift);
            await RequireExecutableSourcesAsync(context, sources, mechanics, enrollment, cancellationToken);
            if (await ScalarAsync(context, "SELECT " + absent, cancellationToken) is true)
            {
                var names = context.GetService<ISqlGenerationHelper>();
                foreach (var source in sources)
                {
                    await ExecuteAsync(context, $"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {names.DelimitIdentifier(enrollment.GlobalLogin)}", cancellationToken);
                    foreach (string privilege in new[] { "SELECT", "INSERT", "UPDATE" })
                    {
                        string[] columns = source.Worker.Where(grant => grant.Privilege == privilege).Select(grant => grant.Column).ToArray();
                        if (columns.Length != 0)
                            await ExecuteAsync(context, $"GRANT {privilege} ({string.Join(',', columns.Select(names.DelimitIdentifier))}) ON {source.Table} TO {names.DelimitIdentifier(enrollment.GlobalLogin)}", cancellationToken);
                    }
                    foreach (string policy in Policies(source))
                    {
                        string operation = policy == ReadPolicy ? "SELECT" : "INSERT";
                        string clause = policy == ReadPolicy ? "USING" : "WITH CHECK";
                        string restriction = policy == InsertRestriction ? "RESTRICTIVE" : "PERMISSIVE";
                        await ExecuteAsync(context, $"CREATE POLICY {names.DelimitIdentifier(policy)} ON {source.Table} AS {restriction} FOR {operation} TO {names.DelimitIdentifier(enrollment.GlobalLogin)} {clause} ({SealedSession(enrollment.GlobalLogin)})", cancellationToken);
                        string hash = await ScalarAsync(context, $"SELECT COALESCE(pg_catalog.md5(p.polqual::text),'') || ':' || COALESCE(pg_catalog.md5(p.polwithcheck::text),'') FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(policy)}", cancellationToken) as string
                            ?? throw new InvalidOperationException(Drift);
                        await ExecuteAsync(context, $"COMMENT ON POLICY {names.DelimitIdentifier(policy)} ON {source.Table} IS {Text(PolicyStamp(source, enrollment.GlobalLogin, policy) + hash)}", cancellationToken);
                    }
                }
                if (await ScalarAsync(context, "SELECT " + complete, cancellationToken) is not true)
                    throw new InvalidOperationException(Drift);
            }
        }
        catch
        {
            await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            throw;
        }
        finally { await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None); }
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    public static async Task RequireCurrentAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        IdentityDatabaseTopology identityTopology, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        if (!Enum.IsDefined(identityTopology)) throw new ArgumentOutOfRangeException(nameof(identityTopology));
        if (enrollment is null) throw new UnauthorizedAccessException(NotReady);
        enrollment.Validate();
        context.RequireBoundTenantLease();
        if (context.Database.GetDbConnection() is not NpgsqlConnection connection || connection.State != ConnectionState.Open)
            throw new UnauthorizedAccessException(NotReady);
        var lease = context.ContextId;
        var tenant = context.TenantContext;
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        int backend = connection.ProcessID;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs args) => interrupted = true;
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (context.ContextId != lease || !ReferenceEquals(tenant, context.TenantContext)
                || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || System.Transactions.Transaction.Current is not null
                || interrupted || !ReferenceEquals(connection, context.Database.GetDbConnection())
                || connection.State != ConnectionState.Open || connection.ProcessID != backend
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())
                || transaction is not null && !ReferenceEquals(transaction.Connection, connection))
                throw new UnauthorizedAccessException(NotReady);
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            if (await ScalarAsync(context, $"SELECT ({SealedSession(enrollment.GlobalLogin)}) AND pg_catalog.current_setting('role') IN ('none',session_user::text) AND pg_catalog.current_setting('session_replication_role')='origin'", cancellationToken) is not true)
                throw new UnauthorizedAccessException(NotReady);
            RequireOriginal();
            var sources = Sources(context);
            string predicate = CataloguePredicate(context, sources, enrollment, true);
            if (await ScalarAsync(context, $"SELECT ({SealedSession(enrollment.GlobalLogin)}) AND pg_catalog.current_setting('role') IN ('none',session_user::text) AND pg_catalog.current_setting('session_replication_role')='origin' AND ({predicate})", cancellationToken) is not true)
                throw new UnauthorizedAccessException(NotReady);
            RequireOriginal();
            await RequireExecutableSourcesAsync(context, sources,
                ProvisioningSharedWriterAssertions.ManagedTenantWorkerCreationInventory(context, enrollment, identityTopology), enrollment, cancellationToken);
            RequireOriginal();
        }
        finally { connection.StateChange -= Changed; }
    }

    internal static bool TryGetGlobalColumns(ExploreDbContext context, Type type,
        out IReadOnlyList<string> read, out IReadOnlyList<string> insert, out IReadOnlyList<string> update)
    {
        var source = Sources(context).SingleOrDefault(source => source.Type == type);
        IReadOnlyList<string> Columns(string privilege) => source is null ? []
            : Array.AsReadOnly(source.Worker.Where(grant => grant.Privilege == privilege).Select(grant => grant.Column).ToArray());
        read = Columns("SELECT");
        insert = Columns("INSERT");
        update = Columns("UPDATE");
        return source is not null;
    }

    /// <summary>The leaf never invokes another permission owner's composed admission predicate.</summary>
    internal static string OptionalGlobalCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var sources = Sources(context);
        return $"(({AbsentPredicate(sources, enrollment)}) OR ({CataloguePredicate(context, sources, enrollment, true)}))";
    }

    internal static string CompleteGlobalCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        CataloguePredicate(context, Sources(context), enrollment, true);

    internal static string GlobalPolicyException(ExploreDbContext context, string login)
    {
        string policies = string.Join(" OR ", Sources(context).SelectMany(source => Policies(source)
            .Select(policy => $"(n.nspname={Text(source.Schema)} AND c.relname={Text(source.Name)} AND ({PolicyPredicate(source, login, policy)}))")));
        return $"COALESCE((session_user={Text(login)} AND current_user=session_user AND ({(policies.Length == 0 ? "false" : policies)})),false)";
    }

    internal static string SourcePolicyPredicate(ExploreDbContext context, Type type, string login)
    {
        var source = Sources(context).SingleOrDefault(value => value.Type == type);
        if (source is null) return "false";
        string policies = string.Join(" OR ", Policies(source).Select(policy => $"({PolicyPredicate(source, login, policy)})"));
        return policies.Length == 0 ? "false" : policies;
    }

    private static string CataloguePredicate(ExploreDbContext context, Source[] sources,
        PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string login = Text(enrollment.GlobalLogin), owner = Text(enrollment.MigratorLogin);
        string expected = "VALUES " + string.Join(',', sources.SelectMany(source => source.Allowed
            .Select(grant => $"({Text(source.Table)},{Text(grant.Column)},{Text(grant.Privilege)})")));
        string required = complete ? $"""
            AND NOT EXISTS (SELECT 1 FROM expected e WHERE NOT EXISTS (
                SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass(e.relation) AND a.attname=e.column_name
                    AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={login})
                    AND acl.grantor=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={owner})
                    AND acl.privilege_type=e.privilege AND NOT acl.is_grantable))
            """ : "";
        string tables = "ARRAY[" + string.Join(',', sources.Select(source => Text(source.Table))) + "]::text[]";
        string federation = Text(sources.Single(source => source.Type == typeof(AtprotoFederationTenantSetting)).Table);
        return $"""
            (WITH expected(relation,column_name,privilege) AS ({expected}),
            members AS (SELECT r.* FROM pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role({login},r.oid,'MEMBER'))
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={login} AND rolcanlogin)
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={owner} AND rolcanlogin)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={login}))
                AND NOT EXISTS (SELECT 1 FROM members r WHERE r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                    OR r.rolname={owner} OR EXISTS (SELECT 1 FROM unnest(ARRAY[{string.Join(',', sources.Select(source => Text(source.Schema)).Distinct())}]::text[]) s(name)
                        WHERE pg_catalog.has_schema_privilege(r.oid,s.name,'CREATE')))
                AND NOT EXISTS (SELECT 1 FROM unnest({tables}) source(relation)
                    JOIN pg_catalog.pg_class t ON t.oid=pg_catalog.to_regclass(source.relation) CROSS JOIN members r
                    WHERE r.oid=t.relowner
                        OR EXISTS (SELECT 1 FROM (VALUES ('SELECT'),('INSERT'),('UPDATE'),('DELETE'),('TRUNCATE'),('REFERENCES'),('TRIGGER'),('MAINTAIN')) permission(privilege)
                            WHERE (CASE WHEN permission.privilege='MAINTAIN'
                                THEN {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", false)}
                                ELSE pg_catalog.has_table_privilege(r.oid,t.oid,permission.privilege) END)
                                AND NOT (source.relation={federation} AND r.rolname={login} AND permission.privilege='DELETE'
                                    AND NOT pg_catalog.has_table_privilege(r.oid,t.oid,'DELETE WITH GRANT OPTION')
                                    AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(t.relacl) acl WHERE acl.grantee=r.oid AND acl.grantor=t.relowner AND acl.privilege_type='DELETE' AND NOT acl.is_grantable)
                                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(t.relacl) acl WHERE acl.grantee=0 AND acl.privilege_type='DELETE')))
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                            WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                                AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                    OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege) AND NOT (
                                        r.rolname={login}
                                        AND EXISTS (SELECT 1 FROM expected e WHERE e.relation=source.relation AND e.column_name=a.attname AND e.privilege=permission.privilege)
                                        AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=r.oid AND acl.grantor=t.relowner AND acl.privilege_type=permission.privilege AND NOT acl.is_grantable)
                                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=0 AND acl.privilege_type=permission.privilege))))))
                AND ({string.Join(" AND ", sources.Select(source => SourcePredicate(context, source, enrollment, complete)))})
                {required})
            """;
    }

    private static string AbsentPredicate(Source[] sources, PostgresNativeQueueEnrollment enrollment) =>
        string.Join(" AND ", sources.Select(source =>
        {
            string baseline = string.Join(" OR ", source.Baseline.Select(grant =>
                $"(a.attname={Text(grant.Column)} AND acl.privilege_type={Text(grant.Privilege)})"));
            return $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r
                    WHERE t.oid=pg_catalog.to_regclass({Text(source.Table)})
                        AND pg_catalog.pg_has_role({Text(enrollment.GlobalLogin)},r.oid,'MEMBER')
                        AND (r.oid=t.relowner OR (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,TRUNCATE,REFERENCES,TRIGGER')
                            OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", false)})
                            OR (pg_catalog.has_table_privilege(r.oid,t.oid,'DELETE') AND NOT (
                                {(source.Type == typeof(AtprotoFederationTenantSetting) ? "true" : "false")}
                                AND r.rolname={Text(enrollment.GlobalLogin)}
                                AND NOT pg_catalog.has_table_privilege(r.oid,t.oid,'DELETE WITH GRANT OPTION')
                                AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(t.relacl) acl WHERE acl.grantee=r.oid AND acl.grantor=t.relowner AND acl.privilege_type='DELETE' AND NOT acl.is_grantable)
                                AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(t.relacl) acl WHERE acl.grantee=0 AND acl.privilege_type='DELETE')))))
                AND
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                    WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)})
                        AND (acl.grantee=0 OR acl.grantee IN (SELECT r.oid FROM pg_catalog.pg_roles r
                            WHERE pg_catalog.pg_has_role({Text(enrollment.GlobalLogin)},r.oid,'MEMBER')))
                        AND NOT (acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.GlobalLogin)})
                            AND NOT acl.is_grantable AND ({(baseline.Length == 0 ? "false" : baseline)})))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)})
                    AND p.polname IN ({Text(ReadPolicy)},{Text(InsertPolicy)},{Text(InsertRestriction)}))
                """;
        }));

    private static string SourcePredicate(ExploreDbContext context, Source source, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        if (source.Type == typeof(ManagedTenantProvisioningPolicySetting))
            return PostgresProvisioningPolicyPermissions.CompleteCataloguePredicate(context, enrollment);

        string columns = string.Join(" AND ", source.Columns.Values.Select(column =>
        {
            string shape = column.System
                ? "a.attnum=-2 AND a.attname='xmin' AND a.atttypid='pg_catalog.xid'::regtype"
                : $"a.attnum>0 AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(column.StoreType)} AND a.attnotnull={(column.Required ? "true" : "false")} AND a.attidentity='' AND a.attgenerated=''";
            return $"EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attname={Text(column.Name)} AND NOT a.attisdropped AND ({shape}))";
        }));
        var metadata = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .SingleOrDefault(table => table.TableName == source.Name && (table.Schema ?? source.Schema) == source.Schema);
        string tenant = metadata is null ? "NOT c.relrowsecurity AND NOT c.relforcerowsecurity" : $"""
            c.relrowsecurity AND c.relforcerowsecurity AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                WHERE p.polrelid=c.oid AND p.polname='tenant_isolation' AND p.polcmd='*' AND p.polpermissive
                    AND p.polroles=ARRAY[0::oid] AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                    AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(metadata, source.Schema))}
                        || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
            """;
        string[] baseline = BaselinePolicies(context, source, enrollment);
        string[] own = Policies(source).Select(policy => PolicyPredicate(source, enrollment.GlobalLogin, policy)).ToArray();
        string allowed = string.Join(" OR ", baseline.Concat(own).Select(policy => $"({policy})"));
        string required = complete ? string.Concat(baseline.Concat(own).Select(policy =>
            $" AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {policy})")) : "";
        return $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass({Text(source.Table)})
                AND c.relkind='r' AND c.relpersistence='p' AND NOT c.relispartition
                AND c.relowner=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.MigratorLogin)})
                AND ({tenant})) AND ({columns})
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)})
                AND p.polname<>'tenant_isolation' AND NOT ({(allowed.Length == 0 ? "false" : allowed)})
                AND (p.polname IN ({Text(ReadPolicy)},{Text(InsertPolicy)},{Text(InsertRestriction)})
                    OR 0::oid=ANY(p.polroles) OR EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                        WHERE pg_catalog.pg_has_role({Text(enrollment.GlobalLogin)},role_id,'MEMBER'))))
            {required}
            """;
    }

    private static string[] BaselinePolicies(ExploreDbContext context, Source source, PostgresNativeQueueEnrollment enrollment)
    {
        if (source.Type == typeof(EventDiscoveryRevision))
            return PostgresDiscoveryMetadataPermissions.RevisionPolicies(context, enrollment).ToArray();
        if (source.Type == typeof(AtprotoFederationTenantSetting))
            return [PostgresAtprotoFederationSettingPermissions.GlobalTenantPolicy(context, enrollment.GlobalLogin)];
        return [.. PostgresTenantCreationPermissions.GlobalSourcePolicies(context, source.Type, enrollment.GlobalLogin),
            .. PostgresUserTenantMembershipReadPermissions.GlobalSourcePolicies(context, source.Type, enrollment.GlobalLogin)];
    }

    private static string[] Policies(Source source)
    {
        if (!source.TenantScoped || source.Core || source.Type == typeof(EventDiscoveryRevision)) return [];
        return [.. source.Worker.Any(grant => grant.Privilege == "SELECT") ? new[] { ReadPolicy } : [],
            .. source.Worker.Any(grant => grant.Privilege == "INSERT") ? new[] { InsertPolicy, InsertRestriction } : []];
    }

    private static string PolicyPredicate(Source source, string login, string policy)
    {
        string role = "'" + login.Replace("'", "''", StringComparison.Ordinal) + "'::name";
        string expression = $"((SESSION_USER = CURRENT_USER) AND (CURRENT_USER = {role}) AND (NULLIF(current_setting('app.current_tenant_id'::text, true), ''::text) IS NULL))";
        return $"""
            p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(policy)}
            AND p.polcmd='{(policy == ReadPolicy ? "r" : "a")}' AND p.polpermissive={(policy == InsertRestriction ? "false" : "true")}
            AND p.polroles=ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})]
            AND {(policy == ReadPolicy ? $"pg_catalog.pg_get_expr(p.polqual,p.polrelid)={Text(expression)} AND p.polwithcheck IS NULL" : $"p.polqual IS NULL AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid)={Text(expression)}")}
            AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PolicyStamp(source, login, policy))}
                || COALESCE(pg_catalog.md5(p.polqual::text),'') || ':' || COALESCE(pg_catalog.md5(p.polwithcheck::text),'')
            """;
    }

    private static string PolicyStamp(Source source, string login, string policy) => "managed-tenant-worker:v1:"
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{source.Schema}\n{source.Name}\n{login}\n{policy}"))) + ":";
    private static string SealedSession(string login) => $"SESSION_USER=CURRENT_USER AND CURRENT_USER={Text(login)}::name AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL";

    private static Source[] Sources(ExploreDbContext context)
    {
        var tenants = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        return ManagedTenantWorkerCreationInventory.Sources.Select(item =>
        {
            var entity = context.Model.FindEntityType(item.Entity) ?? throw new InvalidOperationException("worker_creation_model_missing");
            string name = entity.GetTableName() ?? throw new InvalidOperationException("worker_creation_table_missing");
            string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema ?? entity.GetSchema() ?? "public";
            var store = StoreObjectIdentifier.Table(name, entity.GetSchema());
            if (entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.GetComplexProperties().Any()
                || !item.Insert.IsEmpty && !entity.GetProperties()
                    .Where(property => !(item.Entity == typeof(ManagedTenantProvisioningOperation)
                        && property.Name == nameof(ManagedTenantProvisioningOperation.RowVersion)
                        && property.GetColumnName(store) == "xmin" && property.ClrType == typeof(uint)
                        && property.IsConcurrencyToken && property.ValueGenerated == ValueGenerated.OnAddOrUpdate))
                    .Select(property => property.Name).Order(StringComparer.Ordinal).SequenceEqual(item.Insert.Order(StringComparer.Ordinal)))
                throw new InvalidOperationException("worker_creation_model_shape_drift");
            var columns = entity.GetProperties().ToDictionary(property => property.Name, property =>
            {
                string column = property.GetColumnName(store) ?? throw new InvalidOperationException("worker_creation_column_missing");
                bool system = column == "xmin";
                if (property.IsShadowProperty() || system && (item.Entity != typeof(ManagedTenantProvisioningOperation)
                        && item.Entity != typeof(ManagedControlPlaneRegistration)
                    || property.Name != "RowVersion" || property.ClrType != typeof(uint) || !property.IsConcurrencyToken
                    || property.ValueGenerated != ValueGenerated.OnAddOrUpdate || item.Insert.Contains(property.Name) || item.Update.Contains(property.Name)))
                    throw new InvalidOperationException("worker_creation_column_shape_drift");
                return new Column(column, property.GetRelationalTypeMapping().StoreType, !property.IsNullable, system);
            }, StringComparer.Ordinal);
            Grant[] Grants(IEnumerable<string> properties, string privilege) => properties.Select(property =>
                new Grant(columns.TryGetValue(property, out var column) ? column.Name
                    : throw new InvalidOperationException("worker_creation_property_missing"), privilege)).ToArray();
            Grant[] worker = [.. Grants(item.Read, "SELECT"), .. Grants(item.Insert, "INSERT"), .. Grants(item.Update, "UPDATE")];
            var baseline = new List<Grant>();
            bool core = PostgresTenantCreationPermissions.TryGetGlobalColumns(context, item.Entity, out var coreRead, out var coreInsert);
            baseline.AddRange(coreRead.Select(column => new Grant(column, "SELECT")));
            baseline.AddRange(coreInsert.Select(column => new Grant(column, "INSERT")));
            baseline.AddRange(PostgresTenantMembershipMutationPermissions.GlobalUpdateColumns(context, item.Entity)
                .Select(column => new Grant(column, "UPDATE")));
            if (PostgresProvisioningPolicyPermissions.TryGetGlobalColumns(context, item.Entity,
                    out var policyRead, out var policyUpdate))
            {
                baseline.AddRange(policyRead.Select(column => new Grant(column, "SELECT")));
                baseline.AddRange(policyUpdate.Select(column => new Grant(column, "UPDATE")));
            }
            PostgresUserTenantMembershipReadPermissions.TryGetGlobalReadColumns(context, item.Entity, out var membershipRead);
            baseline.AddRange(membershipRead.Select(column => new Grant(column, "SELECT")));
            if (item.Entity == typeof(User))
            {
                baseline.AddRange(PostgresUserTenantNavigationReadPermissions.GlobalReadColumns(context).Select(column => new Grant(column, "SELECT")));
                baseline.AddRange(PostgresTenantPreferencePermissions.GlobalReadColumns(context).Select(column => new Grant(column, "SELECT")));
                baseline.AddRange(PostgresTenantPreferencePermissions.GlobalUpdateColumns(context).Select(column => new Grant(column, "UPDATE")));
            }
            if (item.Entity == typeof(UserExternalLogin))
                baseline.AddRange(PostgresExternalLoginReadPermissions.GlobalReadColumns(context).Select(column => new Grant(column, "SELECT")));
            if (item.Entity == typeof(EventDiscoveryRevision) || item.Entity == typeof(AtprotoFederationTenantSetting))
            {
                var existing = item.Entity == typeof(EventDiscoveryRevision)
                    ? PostgresDiscoveryMetadataPermissions.GlobalRevisionColumns(context)
                    : PostgresAtprotoFederationSettingPermissions.GlobalTenantColumns(context);
                baseline.AddRange(existing.Read.Select(column => new Grant(column, "SELECT")));
                baseline.AddRange(existing.Insert.Select(column => new Grant(column, "INSERT")));
                baseline.AddRange(existing.Update.Select(column => new Grant(column, "UPDATE")));
            }
            return new Source(item.Entity, schema, name, context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, schema),
                columns, worker, baseline.Distinct().ToArray(), baseline.Concat(worker).Distinct().ToArray(), core,
                tenants.Any(table => table.TableName == name && (table.Schema ?? schema) == schema));
        }).ToArray();
    }

    private static async Task RequireExecutableSourcesAsync(ExploreDbContext context, Source[] sources,
        (ProvisioningSharedWriterAssertions.Writer[] Writers, string UndeclaredAssertion) mechanics,
        PostgresNativeQueueEnrollment enrollment, CancellationToken cancellationToken)
    {
        var names = new HumanPrincipalSourceSql(context);
        var predicates = sources.Select(source =>
        {
            string triggers = string.Join(" OR ", mechanics.Writers
                .Where(writer => writer.Targets.Any(target => target.Type == source.Type))
                .Select(writer => $"(t.tgname={Text(writer.Name)} AND t.tgfoid=pg_catalog.to_regprocedure({Text(names.Name(writer.Function) + "()")}))"));
            string emailCapture = PostgresEmailDeliveryPolicyContract.SettingCapturePredicate(context, enrollment, source.Type);
            return (source.Type, Predicate: $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite WHERE ev_class=pg_catalog.to_regclass({Text(source.Table)}))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits
                    WHERE inhrelid=pg_catalog.to_regclass({Text(source.Table)}) OR inhparent=pg_catalog.to_regclass({Text(source.Table)}))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid=pg_catalog.to_regclass({Text(source.Table)})
                    AND NOT t.tgisinternal AND NOT COALESCE((({(triggers.Length == 0 ? "false" : triggers)}) OR ({emailCapture})),false))
                {(source.Type == typeof(TenantSetting) || source.Type == typeof(SystemSetting)
                    ? $"AND EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE {emailCapture})" : "")}
                AND ({PostgresTenantCreationPermissions.DeclaredForeignKeysPredicate(context, source.Type, source.Table)})
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint f
                    WHERE f.conrelid=pg_catalog.to_regclass({Text(source.Table)}) AND f.contype='f'
                        AND NOT COALESCE((pg_catalog.to_jsonb(f)->>'conenforced')::boolean,true))
                AND ({InsertSurfacePredicate(context, source)})
                """);
        }).ToArray();
        if (await ScalarAsync(context, "SELECT " + string.Join(" AND ", predicates.Select(value => value.Predicate)), cancellationToken) is not true)
        {
            // Fixed model type names identify storage drift without logging SQL or application data.
            foreach (var predicate in predicates)
                if (await ScalarAsync(context, "SELECT " + predicate.Predicate, cancellationToken) is not true)
                    throw new UnauthorizedAccessException(NotReady + ":storage:" + predicate.Type.Name);
            throw new UnauthorizedAccessException(NotReady);
        }
        await ExecuteAsync(context, $"""
            DO $worker_creation_mechanics$ BEGIN
                {mechanics.UndeclaredAssertion}
                {string.Join("\n", mechanics.Writers.Select(writer => writer.AssertInstalled(context)))}
            END $worker_creation_mechanics$;
            """, cancellationToken);
    }

    private static string InsertSurfacePredicate(ExploreDbContext context, Source source)
    {
        if (!source.Worker.Any(grant => grant.Privilege == "INSERT")) return "true";
        if (source.Core)
            return PostgresTenantCreationPermissions.DeclaredInsertSurfacePredicate(context, source.Type);
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(source.Type)!;
        var properties = entity.GetProperties().Where(property => !source.Columns[property.Name].System).ToArray();
        var checks = DeclaredChecks(context, source);
        var modelChecks = entity.GetCheckConstraints().ToArray();
        if (modelChecks.Length != checks.Length || modelChecks.Any(check => !checks.Any(expected =>
            check.Name == expected.Name && string.Equals(check.Sql, expected.ModelSql, StringComparison.Ordinal))))
            throw new InvalidOperationException("managed_tenant_worker_check_model_drift");
        string defaults = string.Join(" AND ", properties.Select(property =>
        {
            string? expected = DeclaredDefault(property);
            string proof = expected is null
                ? "NOT a.atthasdef AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attrdef d WHERE d.adrelid=a.attrelid AND d.adnum=a.attnum)"
                : $"a.atthasdef AND EXISTS (SELECT 1 FROM pg_catalog.pg_attrdef d WHERE d.adrelid=a.attrelid AND d.adnum=a.attnum AND pg_catalog.pg_get_expr(d.adbin,d.adrelid)={Text(expected)} AND ({BuiltinDependencies("pg_attrdef", "d.oid")}))";
            return $"EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attname={Text(source.Columns[property.Name].Name)} AND a.attnum>0 AND NOT a.attisdropped AND a.attidentity='' AND a.attgenerated='' AND ({proof}))";
        }));
        return $"""
            (SELECT count(*)={properties.Length} FROM pg_catalog.pg_attribute
                WHERE attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND attnum>0 AND NOT attisdropped)
            AND ({defaults})
            AND (SELECT count(*)={checks.Length} FROM pg_catalog.pg_constraint
                WHERE conrelid=pg_catalog.to_regclass({Text(source.Table)}) AND contype='c')
            {string.Concat(checks.Select(check => $"""
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid=pg_catalog.to_regclass({Text(source.Table)})
                    AND c.contype='c' AND c.conname={Text(check.Name)}
                    AND COALESCE((pg_catalog.to_jsonb(c)->>'conenforced')::boolean,true) AND c.convalidated AND c.conislocal
                    AND c.coninhcount=0 AND NOT c.connoinherit AND NOT c.condeferrable AND NOT c.condeferred
                    AND pg_catalog.pg_get_expr(c.conbin,c.conrelid)=({check.CatalogueSql})
                    AND ({BuiltinDependencies("pg_constraint", "c.oid")}))
                """))}
            """;
    }

    private static string? DeclaredDefault(IProperty property)
    {
        string? sql = property.GetDefaultValueSql();
        var literal = property.FindAnnotation(RelationalAnnotationNames.DefaultValue);
        if (sql is null && literal is null) return null;
        if (literal is null && string.Equals(sql, "NOW()", StringComparison.OrdinalIgnoreCase)) return "now()";
        if (literal is null && string.Equals(sql, "uuidv7()", StringComparison.Ordinal) && property.ClrType == typeof(Guid)) return "uuidv7()";
        if (sql is null && literal?.Value is bool boolean) return boolean ? "true" : "false";
        if (sql is null && literal?.Value is int integer) return integer.ToString(CultureInfo.InvariantCulture);
        throw new InvalidOperationException("managed_tenant_worker_default_model_drift");
    }

    private static (string Name, string ModelSql, string CatalogueSql)[] DeclaredChecks(ExploreDbContext context, Source source)
    {
        string Column(string property) => source.Columns[property].Name;
        (string Name, string ModelSql, string CatalogueSql) Check(string name, string modelSql, string template)
        {
            // Only these source-owned templates are expanded. PostgreSQL quotes mapped
            // identifiers; the catalogue expression itself is compared without normalization.
            var parts = template.Split('{', '}');
            if (parts.Length % 2 == 0) throw new InvalidOperationException("worker_creation_check_template_invalid");
            string expression = string.Join(" || ", parts.Select((part, index) => index % 2 == 0
                ? Text(part) : $"pg_catalog.quote_ident({Text(Column(part))})"));
            return (name, modelSql, expression);
        }
        if (source.Type == typeof(ManagedTenantProvisioningOperation)) return
        [
            Check("ck_managed_tenant_provisioning_terminal_result",
                "(status = 'Succeeded') = (tenant_id IS NOT NULL AND tenant_administrator_user_id IS NOT NULL AND completed_at IS NOT NULL)",
                "((({Status})::text = 'Succeeded'::text) = (({TenantId} IS NOT NULL) AND ({TenantAdministratorUserId} IS NOT NULL) AND ({CompletedAt} IS NOT NULL)))"),
            Check("ck_managed_tenant_provisioning_failed",
                "(status = 'Failed') = (failure_code IS NOT NULL AND failed_at IS NOT NULL)",
                "((({Status})::text = 'Failed'::text) = (({FailureCode} IS NOT NULL) AND ({FailedAt} IS NOT NULL)))"),
            Check("ck_managed_tenant_provisioning_cancelled",
                "(status = 'Cancelled') = (cancelled_at IS NOT NULL)",
                "((({Status})::text = 'Cancelled'::text) = ({CancelledAt} IS NOT NULL))"),
            Check("ck_managed_tenant_provisioning_request_snapshot",
                "(status IN ('Pending', 'Processing')) = (request_json IS NOT NULL)",
                "((({Status})::text = ANY ((ARRAY['Pending'::character varying, 'Processing'::character varying])::text[])) = ({RequestJson} IS NOT NULL))"),
            Check("ck_managed_tenant_provisioning_outbox_pointer",
                "current_outbox_message_id <> '00000000-0000-0000-0000-000000000000'::uuid",
                "({CurrentOutboxMessageId} <> '00000000-0000-0000-0000-000000000000'::uuid)")
        ];
        if (source.Type == typeof(EventDiscoveryRevision)) return
        [
            Check("ck_discovery_revision_epochs", $"{Column("IdentityEpoch")} >= 0 AND {Column("DisclosureEpoch")} >= 0",
                "(({IdentityEpoch} >= 0) AND ({DisclosureEpoch} >= 0))")
        ];
        if (source.Type == typeof(TenantSetting)) return
        [
            Check("ck_tenant_settings_no_dedicated_atproto_federation",
                $"{Column("SettingKey")} NOT IN ('federation.atproto_events_enabled', 'federation.atproto_events_backfill_enabled', 'federation.atproto_events_backfill_mode')",
                "(({SettingKey})::text <> ALL ((ARRAY['federation.atproto_events_enabled'::character varying, 'federation.atproto_events_backfill_enabled'::character varying, 'federation.atproto_events_backfill_mode'::character varying])::text[]))")
        ];
        if (source.Type == typeof(AtprotoFederationTenantSetting)) return
        [
            Check("ck_atproto_federation_tenant_settings_key", AtprotoFederationInstanceSettingConfiguration.KeyConstraintSql,
                "(((({SettingKey})::text = 'federation.atproto_events_enabled'::text) AND (length(({SettingKey})::text) = 33)) OR ((({SettingKey})::text = 'federation.atproto_events_backfill_enabled'::text) AND (length(({SettingKey})::text) = 42)) OR ((({SettingKey})::text = 'federation.atproto_events_backfill_mode'::text) AND (length(({SettingKey})::text) = 39)))"),
            Check("ck_atproto_federation_tenant_settings_value", AtprotoFederationInstanceSettingConfiguration.ValueConstraintSql,
                """(((({SettingKey})::text = ANY ((ARRAY['federation.atproto_events_enabled'::character varying, 'federation.atproto_events_backfill_enabled'::character varying])::text[])) AND (((({Value})::text = 'true'::text) AND (length(({Value})::text) = 4)) OR ((({Value})::text = 'false'::text) AND (length(({Value})::text) = 5)))) OR ((({SettingKey})::text = 'federation.atproto_events_backfill_mode'::text) AND (((lower(({Value})::text) = '"downtime_only"'::text) AND (length(({Value})::text) = 15)) OR ((lower(({Value})::text) = '"full"'::text) AND (length(({Value})::text) = 6)))))""")
        ];
        if (source.Type == typeof(Actor))
        {
            string owners = string.Join(", ", new[] { "UserId", "OrganizationId", "GroupId", "ExternalActorSubjectId", "ServicePrincipalId" }.Select(Column));
            string externalType = ((int)ActorTypeEnum.ExternalUnclassified).ToString(CultureInfo.InvariantCulture);
            return
            [
                Check("ck_actors_exactly_one_owner",
                    $"num_nonnulls({owners}) = 1 OR ({Column("IsDeleted")} AND num_nonnulls({owners}) = 0)",
                    "((num_nonnulls({UserId}, {OrganizationId}, {GroupId}, {ExternalActorSubjectId}, {ServicePrincipalId}) = 1) OR ({IsDeleted} AND (num_nonnulls({UserId}, {OrganizationId}, {GroupId}, {ExternalActorSubjectId}, {ServicePrincipalId}) = 0)))"),
                Check("ck_actors_external_type_matches_owner",
                    $"({Column("ExternalActorSubjectId")} IS NULL AND {Column("ActorTypeId")} <> {externalType}) OR ({Column("ExternalActorSubjectId")} IS NOT NULL AND {Column("ActorTypeId")} = {externalType})",
                    "((({ExternalActorSubjectId} IS NULL) AND ({ActorTypeId} <> " + externalType + ")) OR (({ExternalActorSubjectId} IS NOT NULL) AND ({ActorTypeId} = " + externalType + ")))")
            ];
        }
        if (source.Type == typeof(ActorPii)) return
        [
            Check("ck_actor_pii_profile_picture_shape",
                $"({Column("ProfilePictureStorageObjectId")} IS NULL OR ({Column("ProfilePictureStorageObjectId")} <> '00000000-0000-0000-0000-000000000000' AND {Column("ExternalProfilePictureUri")} IS NULL)) AND ({Column("ExternalProfilePictureUri")} IS NULL OR (LOWER({Column("ExternalProfilePictureUri")}) LIKE 'https://_%' OR LOWER({Column("ExternalProfilePictureUri")}) LIKE 'http://_%'))",
                "((({ProfilePictureStorageObjectId} IS NULL) OR (({ProfilePictureStorageObjectId} <> '00000000-0000-0000-0000-000000000000'::uuid) AND ({ExternalProfilePictureUri} IS NULL))) AND (({ExternalProfilePictureUri} IS NULL) OR ((lower(({ExternalProfilePictureUri})::text) ~~ 'https://_%'::text) OR (lower(({ExternalProfilePictureUri})::text) ~~ 'http://_%'::text))))")
        ];
        if (source.Type == typeof(ExternalBinding))
        {
            string statuses = string.Join(", ", new[] { ExternalBindingStatusEnum.Active, ExternalBindingStatusEnum.Suspended, ExternalBindingStatusEnum.Archived }
                .Select(status => ((int)status).ToString(CultureInfo.InvariantCulture)));
            string[] textProperties = ["ProviderKey", "ExternalSystem", "ExternalType", "ExternalId", "InternalType"];
            var pairs = ReferenceTypeRegistry.AllExternalBindingPairs
                .OrderBy(pair => pair.ExternalType, StringComparer.Ordinal)
                .ThenBy(pair => pair.InternalType, StringComparer.Ordinal).ToArray();
            string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
            string Scope(ExternalBindingReferenceDefinition pair) => pair.BindingTenantScopeRule == ReferenceTenantScopeRule.Global ? "IS NULL" : "IS NOT NULL";
            string modelPairs = string.Join(" OR ", pairs.Select(pair =>
                $"({Column("ExternalType")} = {Literal(pair.ExternalType)} AND {Column("InternalType")} = {Literal(pair.InternalType)} AND {Column("ScopeTenantId")} {Scope(pair)})"));
            string cataloguePairs = "(" + string.Join(" OR ", pairs.Select(pair =>
                "((({ExternalType})::text = " + Literal(pair.ExternalType) + "::text) AND (({InternalType})::text = "
                + Literal(pair.InternalType) + "::text) AND ({ScopeTenantId} " + Scope(pair) + "))")) + ")";
            return
            [
                Check("ck_external_bindings_status", $"{Column("ExternalBindingStatusId")} IN ({statuses})",
                    "({ExternalBindingStatusId} = ANY (ARRAY[" + statuses + "]))"),
                Check("ck_external_bindings_text_not_blank",
                    string.Join(" AND ", textProperties.Select(property => $"length(btrim({Column(property)})) > 0")),
                    "(" + string.Join(" AND ", textProperties.Select(property => "(length(btrim(({" + property + "})::text)) > 0)")) + ")"),
                Check("ck_external_bindings_registered_pair_scope", modelPairs, cataloguePairs)
            ];
        }
        // A new model expression cannot become executable storage support merely
        // because its name is present in the live catalogue.
        if (!context.GetService<IDesignTimeModel>().Model.FindEntityType(source.Type)!.GetCheckConstraints().Any())
            return [];
        throw new InvalidOperationException("managed_tenant_worker_check_model_not_declared");
    }

    private static string BuiltinDependencies(string catalog, string objectId) => $"""
        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_depend dependency
            WHERE dependency.classid='pg_catalog.{catalog}'::regclass AND dependency.objid={objectId}
                AND ((dependency.refclassid='pg_catalog.pg_proc'::regclass AND NOT EXISTS (
                    SELECT 1 FROM pg_catalog.pg_proc routine WHERE routine.oid=dependency.refobjid AND routine.pronamespace='pg_catalog'::regnamespace))
                    OR (dependency.refclassid='pg_catalog.pg_operator'::regclass AND NOT EXISTS (
                    SELECT 1 FROM pg_catalog.pg_operator operation WHERE operation.oid=dependency.refobjid AND operation.oprnamespace='pg_catalog'::regnamespace))))
        """;

    private sealed record Grant(string Column, string Privilege);
    private sealed record Column(string Name, string StoreType, bool Required, bool System);
    private sealed record Source(Type Type, string Schema, string Name, string Table, Dictionary<string, Column> Columns,
        Grant[] Worker, Grant[] Baseline, Grant[] Allowed, bool Core, bool TenantScoped);

    private static DbCommand Command(ExploreDbContext context, string sql)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? 30;
        command.CommandText = sql;
        return command;
    }
    private static async Task<object?> ScalarAsync(ExploreDbContext context, string sql, CancellationToken cancellationToken)
    {
        await using var command = Command(context, sql);
        return await command.ExecuteScalarAsync(cancellationToken);
    }
    private static async Task ExecuteAsync(ExploreDbContext context, string sql, CancellationToken cancellationToken)
    {
        await using var command = Command(context, sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
