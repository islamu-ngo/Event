using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Explore.Persistence.Schema;

/// <summary>Fixed discovery epoch and tenant-key permissions; API code owns mutation decisions.</summary>
public static class PostgresDiscoveryMetadataPermissions
{
    private const string ReadPolicy = "discovery_metadata_global_read";
    private const string UpdatePolicy = "discovery_metadata_global_update";
    private const string InsertPolicy = "discovery_metadata_global_insert";
    private const string RuntimeInsertPolicy = "discovery_metadata_runtime_insert";
    private const string Drift = "discovery_metadata_permissions_drift";
    private static readonly string[] RevisionProperties = ["Id", "TenantId", "IdentityEpoch", "DisclosureEpoch"];
    private static readonly string[] EpochProperties = ["IdentityEpoch", "DisclosureEpoch"];

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        var revision = Revision(context);
        var tenant = Map(context, typeof(Tenant), ["Id"]);
        string fresh = FreshPredicate(revision, enrollment);
        string complete = ManifestPredicate(context, revision, tenant, enrollment, complete: true);
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
        string savepoint = "discovery_metadata_" + Guid.CreateVersion7().ToString("N");
        await transaction.CreateSavepointAsync(savepoint, cancellationToken);
        try
        {
            if (await ScalarAsync(context, $"SELECT session_user={Text(enrollment.MigratorLogin)} AND current_user=session_user AND pg_catalog.current_setting('role') IN ('none',session_user::text)", cancellationToken) is not true)
                throw new UnauthorizedAccessException("discovery_metadata_installer_wrong_identity");
            if (await ScalarAsync(context, $"SELECT ({ManifestPredicate(context, revision, tenant, enrollment, complete: false)}) AND (({fresh}) OR ({complete}))", cancellationToken) is not true)
                throw new InvalidOperationException(Drift);
            if (await ScalarAsync(context, "SELECT " + fresh, cancellationToken) is true)
            {
                var names = context.GetService<ISqlGenerationHelper>();
                foreach (string login in new[] { enrollment.RuntimeLogin, enrollment.GlobalLogin })
                    await ExecuteAsync(context, $"""
                        GRANT USAGE ON SCHEMA {names.DelimitIdentifier(revision.Schema)} TO {names.DelimitIdentifier(login)};
                        GRANT SELECT ({string.Join(',', revision.Columns.Values.Select(names.DelimitIdentifier))}) ON {revision.Table} TO {names.DelimitIdentifier(login)};
                        GRANT INSERT ({string.Join(',', revision.Columns.Values.Select(names.DelimitIdentifier))}) ON {revision.Table} TO {names.DelimitIdentifier(login)};
                        GRANT UPDATE ({string.Join(',', EpochProperties.Select(property => names.DelimitIdentifier(revision.Columns[property])))}) ON {revision.Table} TO {names.DelimitIdentifier(login)};
                        """, cancellationToken);
                await ExecuteAsync(context, $"""
                    GRANT USAGE ON SCHEMA {names.DelimitIdentifier(tenant.Schema)} TO {names.DelimitIdentifier(enrollment.GlobalLogin)};
                    GRANT SELECT ({names.DelimitIdentifier(tenant.Columns["Id"])}) ON {tenant.Table} TO {names.DelimitIdentifier(enrollment.GlobalLogin)};
                    CREATE POLICY {names.DelimitIdentifier(ReadPolicy)} ON {revision.Table} FOR SELECT
                        TO {names.DelimitIdentifier(enrollment.GlobalLogin)} USING ({SealedSession(enrollment.GlobalLogin)});
                    CREATE POLICY {names.DelimitIdentifier(UpdatePolicy)} ON {revision.Table} FOR UPDATE
                        TO {names.DelimitIdentifier(enrollment.GlobalLogin)} USING ({SealedSession(enrollment.GlobalLogin)})
                        WITH CHECK ({SealedSession(enrollment.GlobalLogin)});
                    CREATE POLICY {names.DelimitIdentifier(InsertPolicy)} ON {revision.Table} FOR INSERT
                        TO {names.DelimitIdentifier(enrollment.GlobalLogin)} WITH CHECK ({SealedSession(enrollment.GlobalLogin)});
                    CREATE POLICY {names.DelimitIdentifier(RuntimeInsertPolicy)} ON {revision.Table} FOR INSERT
                        TO {names.DelimitIdentifier(enrollment.RuntimeLogin)} WITH CHECK ({SealedSession(enrollment.RuntimeLogin)});
                    """, cancellationToken);
                foreach (var (policy, login) in Policies(enrollment))
                {
                    string hash = await ScalarAsync(context, $"""
                        SELECT COALESCE(pg_catalog.md5(p.polqual::text),'') || ':' || COALESCE(pg_catalog.md5(p.polwithcheck::text),'')
                        FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(revision.Table)}) AND p.polname={Text(policy)}
                        """, cancellationToken) as string ?? throw new InvalidOperationException(Drift);
                    await ExecuteAsync(context, $"COMMENT ON POLICY {names.DelimitIdentifier(policy)} ON {revision.Table} IS {Text(PolicyStamp(revision, login, policy) + hash)}", cancellationToken);
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

    /// <summary>
    /// Attests the complete metadata contract on the caller's open original owner, without opening
    /// a connection or acquiring a transaction. Mutation admission remains the caller's responsibility.
    /// </summary>
    internal static async Task RequireCurrentAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        bool globalOnly, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null)
            throw new UnauthorizedAccessException("Discovery enrollment is missing.");
        enrollment.Validate();
        context.RequireBoundTenantLease();
        var tenantIdentity = context.TenantContext;
        Guid? tenantId = context.TenantFilterTenantId;
        if (context.Database.GetDbConnection() is not NpgsqlConnection connection || connection.State != ConnectionState.Open)
            throw new UnauthorizedAccessException("discovery_metadata_open_owner_required");
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        int backend = connection.ProcessID;
        ValidateOwner();
        var revision = Revision(context);
        var tenant = Map(context, typeof(Tenant), ["Id"]);
        string identity = globalOnly ? $"session_user={Text(enrollment.GlobalLogin)}"
            : $"(session_user={Text(enrollment.GlobalLogin)} OR (session_user={Text(enrollment.RuntimeLogin)} AND {Text(tenantId?.ToString() ?? "")}<>''))";
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? 30;
        command.CommandText = $"""
            SELECT ({identity}) AND current_user=session_user
                AND pg_catalog.current_setting('role') IN ('none',session_user::text)
                AND pg_catalog.current_setting('session_replication_role')='origin'
                AND COALESCE(pg_catalog.current_setting('app.current_tenant_id',true),'')={Text(tenantId?.ToString() ?? "")}
                AND ({ManifestPredicate(context, revision, tenant, enrollment, complete: true)})
            """;
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new UnauthorizedAccessException("discovery_metadata_current_owner_not_ready");
        ValidateOwner();

        void ValidateOwner()
        {
            context.RequireBoundTenantLease();
            if (context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
                || tenantId == Guid.Empty || globalOnly && tenantId is not null
                || !ReferenceEquals(tenantIdentity, context.TenantContext) || tenantId != context.TenantFilterTenantId
                || System.Transactions.Transaction.Current is not null
                || !ReferenceEquals(connection, context.Database.GetDbConnection()) || connection.State != ConnectionState.Open
                || connection.ProcessID != backend
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())
                || transaction is not null && !ReferenceEquals(transaction.Connection, connection))
                throw new UnauthorizedAccessException("discovery_metadata_original_owner_required");
        }
    }

    /// <summary>Pure mapped baseline for the admitted-worker catalogue leaf, not caller admission.</summary>
    internal static (IReadOnlyList<string> Read, IReadOnlyList<string> Insert, IReadOnlyList<string> Update)
        GlobalRevisionColumns(ExploreDbContext context)
    {
        var revision = Revision(context);
        return (Array.AsReadOnly(RevisionProperties.Select(property => revision.Columns[property]).ToArray()),
            Array.AsReadOnly(RevisionProperties.Select(property => revision.Columns[property]).ToArray()),
            Array.AsReadOnly(EpochProperties.Select(property => revision.Columns[property]).ToArray()));
    }

    internal static IReadOnlyList<string> RevisionPolicies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var revision = Revision(context);
        return Array.AsReadOnly(Policies(enrollment)
            .Select(policy => PolicyPredicate(revision, policy.Login, policy.Name)).ToArray());
    }

    /// <summary>Exact Global-only policy exception for the existing readiness query's p/c/n aliases.</summary>
    internal static string GlobalPolicyException(ExploreDbContext context, string enrolledGlobalLogin)
    {
        var revision = Revision(context);
        return $"""
            COALESCE((session_user={Text(enrolledGlobalLogin)} AND current_user=session_user
                AND pg_catalog.current_setting('role') IN ('none',session_user::text)
                AND n.nspname={Text(revision.Schema)} AND c.relname={Text(revision.Name)}
                AND ({PolicyPredicate(revision, enrolledGlobalLogin, ReadPolicy)} OR {PolicyPredicate(revision, enrolledGlobalLogin, UpdatePolicy)}
                    OR {PolicyPredicate(revision, enrolledGlobalLogin, InsertPolicy)})),false)
            """;
    }

    /// <summary>Exact Runtime INSERT-only exception; selected tenants still use the existing equality policy.</summary>
    internal static string RuntimePolicyException(ExploreDbContext context, string enrolledRuntimeLogin)
    {
        var revision = Revision(context);
        return $"""
            COALESCE((session_user={Text(enrolledRuntimeLogin)} AND current_user=session_user
                AND pg_catalog.current_setting('role') IN ('none',session_user::text)
                AND n.nspname={Text(revision.Schema)} AND c.relname={Text(revision.Name)}
                AND ({PolicyPredicate(revision, enrolledRuntimeLogin, RuntimeInsertPolicy)})),false)
            """;
    }

    /// <summary>Optional existing revision authority only; native consumers do not require metadata installation.</summary>
    internal static string RevisionAuthorityPredicate(ExploreDbContext context, string login)
    {
        var revision = Revision(context);
        return AuthorityPredicate([revision], [(revision, login, RevisionProperties, RevisionProperties, EpochProperties)], complete: false);
    }

    /// <summary>One fixed Global Tenant.Id read, composed only by owners that actually source Tenant.</summary>
    internal static string GlobalTenantIdReadPredicate(HumanPrincipalSourceSql names, string global) => $"""
        t.oid={Text(names.Table(typeof(Tenant)))}::regclass AND r.rolname={Text(global)} AND a.attnum>0
        AND a.attname={Text(names.ColumnName(typeof(Tenant), nameof(Tenant.Id)))}
        AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
            WHERE acl.grantee={RoleOid(global)} AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=0 AND acl.privilege_type='SELECT')
        """;

    private static string ManifestPredicate(ExploreDbContext context, Source revision, Source tenant,
        PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string runtime = Text(enrollment.RuntimeLogin), global = Text(enrollment.GlobalLogin);
        string policies = string.Join(" OR ", Policies(enrollment)
            .Select(policy => "(" + PolicyPredicate(revision, policy.Login, policy.Name) + ")"));
        string required = complete ? string.Join(" AND ", Policies(enrollment).Select(policy => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {PolicyPredicate(revision, policy.Login, policy.Name)})
            """)) + $"""
            AND pg_catalog.has_schema_privilege({runtime},{Text(revision.Schema)},'USAGE')
            AND pg_catalog.has_schema_privilege({global},{Text(revision.Schema)},'USAGE')
            AND pg_catalog.has_schema_privilege({global},{Text(tenant.Schema)},'USAGE')
            """ : "true";
        string columns = string.Join(" AND ", RevisionProperties.Select(property => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=pg_catalog.to_regclass({Text(revision.Table)})
                AND a.attname={Text(revision.Columns[property])} AND a.attnum>0 AND NOT a.attisdropped
                AND a.attnotnull AND a.attgenerated='' AND a.attidentity=''
                AND a.atttypid={Text(property is "Id" or "TenantId" ? "uuid" : "bigint")}::regtype)
            """));
        var metadata = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model).Single(table =>
            table.TableName == revision.Name && (table.Schema ?? revision.Schema) == revision.Schema);
        string fingerprint = PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(metadata, revision.Schema);
        return $"""
            ({AuthorityPredicate([revision, tenant], [(revision, enrollment.RuntimeLogin, RevisionProperties, RevisionProperties, EpochProperties),
                (revision, enrollment.GlobalLogin, RevisionProperties, RevisionProperties, EpochProperties), (tenant, enrollment.GlobalLogin, ["Id"], [], [])], complete,
                context, enrollment)})
            AND (SELECT count(*)=2 FROM pg_catalog.pg_roles WHERE rolname IN ({runtime},{global}) AND rolcanlogin)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid IN ({RoleOid(enrollment.RuntimeLogin)},{RoleOid(enrollment.GlobalLogin)}))
            AND NOT EXISTS (SELECT 1 FROM unnest(ARRAY[{Text(revision.Table)},{Text(tenant.Table)}]) relation
                LEFT JOIN pg_catalog.pg_class c ON c.oid=pg_catalog.to_regclass(relation)
                WHERE c.oid IS NULL OR c.relkind<>'r' OR c.relpersistence<>'p' OR c.relowner<>{RoleOid(enrollment.MigratorLogin)})
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass({Text(tenant.Table)})
                AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits WHERE inhrelid IN (pg_catalog.to_regclass({Text(revision.Table)}),pg_catalog.to_regclass({Text(tenant.Table)}))
                OR inhparent IN (pg_catalog.to_regclass({Text(revision.Table)}),pg_catalog.to_regclass({Text(tenant.Table)})))
            AND {columns}
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=pg_catalog.to_regclass({Text(revision.Table)})
                AND a.attnum>0 AND NOT a.attisdropped AND a.attname<>ALL(ARRAY[{string.Join(',', revision.Columns.Values.Select(Text))}]::text[]))
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_policy p ON p.polrelid=c.oid
                WHERE c.oid=pg_catalog.to_regclass({Text(revision.Table)}) AND c.relrowsecurity AND c.relforcerowsecurity
                    AND p.polname='tenant_isolation' AND p.polcmd='*' AND p.polpermissive AND p.polroles=ARRAY[0::oid]
                    AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                    AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(fingerprint)} || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(revision.Table)})
                AND p.polname<>'tenant_isolation' AND NOT COALESCE(({policies}),false)
                AND (p.polname IN ({string.Join(',', Policies(enrollment).Select(policy => Text(policy.Name)))}) OR 0::oid=ANY(p.polroles)
                    OR EXISTS (SELECT 1 FROM unnest(p.polroles) role_id WHERE pg_catalog.pg_has_role({runtime},role_id,'MEMBER') OR pg_catalog.pg_has_role({global},role_id,'MEMBER'))))
            AND ({required})
            """;
    }

    // Only the enrolled account's closure is examined. Independent native source-owner roles are not ours to attest.
    private static string AuthorityPredicate(Source[] sources,
        (Source Source, string Login, string[] Read, string[] Insert, string[] Write)[] permissions, bool complete,
        ExploreDbContext? context = null, PostgresNativeQueueEnrollment? creatorEnrollment = null)
    {
        string expected = "VALUES " + string.Join(',', permissions.SelectMany(permission =>
            permission.Read.Select(property => (property, Privilege: "SELECT"))
                .Concat(permission.Insert.Select(property => (property, Privilege: "INSERT")))
                .Concat(permission.Write.Select(property => (property, Privilege: "UPDATE")))
                .Select(grant => $"({Text(permission.Source.Table)},{Text(permission.Source.Columns[grant.property])},{Text(permission.Login)},{Text(grant.Privilege)})")));
        // Other owners contribute only fixed mapped columns, never their composed admission predicates.
        string[] declaredGlobalGrants = context is null || creatorEnrollment is null ? [] : sources.SelectMany(source =>
        {
            PostgresTenantCreationPermissions.TryGetGlobalColumns(context, source.Type, out var read, out var insert);
            PostgresUserTenantMembershipReadPermissions.TryGetGlobalReadColumns(context, source.Type, out var membershipRead);
            return read.Concat(membershipRead).Distinct(StringComparer.Ordinal).Select(column => (column, Privilege: "SELECT"))
                .Concat(insert.Select(column => (column, Privilege: "INSERT")))
                .Select(grant => $"({Text(source.Table)},{Text(grant.column)},{Text(creatorEnrollment.GlobalLogin)},{Text(grant.Privilege)})");
        }).ToArray();
        string[] declaredRuntimeParents = context is null || creatorEnrollment is null ? [] : sources.SelectMany(source =>
            PostgresTenantApiKeyStoragePermissions.RuntimeParentReadColumns(context, source.Type)
                .Select(column => $"({Text(source.Table)},{Text(column)},{Text(creatorEnrollment.RuntimeLogin)},'SELECT')")).ToArray();
        string[] declaredGrants = [.. declaredGlobalGrants, .. declaredRuntimeParents];
        string permitted = "SELECT * FROM expected" + (declaredGrants.Length == 0 ? "" : " UNION VALUES " + string.Join(',', declaredGrants));
        string tables = "ARRAY[" + string.Join(',', sources.Select(source => Text(source.Table))) + "]::text[]";
        string required = complete ? """
            AND NOT EXISTS (SELECT 1 FROM expected e WHERE NOT EXISTS (
                SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass(e.relation) AND a.attname=e.column_name
                    AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=e.login)
                    AND acl.grantor=(SELECT relowner FROM pg_catalog.pg_class WHERE oid=a.attrelid)
                    AND acl.privilege_type=e.privilege AND NOT acl.is_grantable))
            """ : "";
        return $"""
            (WITH expected(relation,column_name,login,privilege) AS ({expected}),
            permitted(relation,column_name,login,privilege) AS ({permitted}),
            callers AS (SELECT DISTINCT login FROM expected), members AS (
                SELECT callers.login,r.* FROM callers CROSS JOIN pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role(callers.login,r.oid,'MEMBER'))
            SELECT NOT EXISTS (SELECT 1 FROM members r WHERE r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                OR EXISTS (SELECT 1 FROM unnest(ARRAY[{string.Join(',', sources.Select(source => Text(source.Schema)).Distinct())}]::text[]) schema_name
                    WHERE pg_catalog.has_schema_privilege(r.oid,schema_name,'CREATE')))
            AND NOT EXISTS (SELECT 1 FROM unnest({tables}) source(relation) JOIN pg_catalog.pg_class t ON t.oid=pg_catalog.to_regclass(source.relation)
                CROSS JOIN members r WHERE r.oid=t.relowner
                    OR (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", false)})
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                        WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                            AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege) AND NOT (
                                    a.attnum>0 AND r.rolname=r.login
                                    AND EXISTS (SELECT 1 FROM permitted e WHERE e.relation=source.relation AND e.column_name=a.attname AND e.login=r.login AND e.privilege=permission.privilege)
                                    AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=r.oid AND acl.grantor=t.relowner AND acl.privilege_type=permission.privilege AND NOT acl.is_grantable)
                                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=0 AND acl.privilege_type=permission.privilege))))))
            {required})
            """;
    }

    // Parent-directory grants may precede metadata installation; AuthorityPredicate checks their exact inventory.
    private static string FreshPredicate(Source revision, PostgresNativeQueueEnrollment enrollment) => $"""
        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
            WHERE a.attrelid=pg_catalog.to_regclass({Text(revision.Table)})
                AND acl.grantee IN ({RoleOid(enrollment.RuntimeLogin)},{RoleOid(enrollment.GlobalLogin)}))
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(revision.Table)})
            AND p.polname IN ({string.Join(',', Policies(enrollment).Select(policy => Text(policy.Name)))}))
        """;

    private static (string Name, string Login)[] Policies(PostgresNativeQueueEnrollment enrollment) =>
    [
        (ReadPolicy, enrollment.GlobalLogin), (UpdatePolicy, enrollment.GlobalLogin),
        (InsertPolicy, enrollment.GlobalLogin), (RuntimeInsertPolicy, enrollment.RuntimeLogin)
    ];

    private static string SealedSession(string login) => $"""
        SESSION_USER = CURRENT_USER AND CURRENT_USER = {Text(login)}::name
        AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL
        """;

    private static string PolicyPredicate(Source revision, string login, string policy)
    {
        string command = policy switch
        {
            ReadPolicy => "r",
            UpdatePolicy => "w",
            InsertPolicy or RuntimeInsertPolicy => "a",
            _ => throw new ArgumentOutOfRangeException(nameof(policy))
        };
        // PostgreSQL 18 with standard_conforming_strings=on deparses standard
        // literals, unlike quote_literal's E-prefixed backslash literals. Encode
        // expected text separately from executable installation E-literals.
        string roleLiteral = "'" + login.Replace("'", "''", StringComparison.Ordinal) + "'";
        string expression = Text("((SESSION_USER = CURRENT_USER) AND (CURRENT_USER = " + roleLiteral
            + "::name) AND (NULLIF(current_setting('app.current_tenant_id'::text, true), ''::text) IS NULL))");
        return $"""
            p.polrelid=pg_catalog.to_regclass({Text(revision.Table)}) AND p.polname={Text(policy)}
            AND p.polcmd='{command}' AND p.polpermissive
            AND p.polroles=ARRAY[{RoleOid(login)}]
            AND {(command == "a" ? "p.polqual IS NULL" : $"pg_catalog.pg_get_expr(p.polqual,p.polrelid)=({expression})")}
            AND {(command == "r" ? "p.polwithcheck IS NULL" : $"pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid)=({expression})")}
            AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PolicyStamp(revision, login, policy))}
                || COALESCE(pg_catalog.md5(p.polqual::text),'') || ':' || COALESCE(pg_catalog.md5(p.polwithcheck::text),'')
            """;
    }

    private static string PolicyStamp(Source source, string login, string policy) => "discovery-metadata:v1:"
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{source.Schema}\n{source.Name}\n{login}\n{policy}"))) + ":";

    private static Source Revision(ExploreDbContext context)
    {
        var source = Map(context, typeof(EventDiscoveryRevision), RevisionProperties);
        var entity = context.Model.FindEntityType(typeof(EventDiscoveryRevision))!;
        if (!entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).SequenceEqual(RevisionProperties.Order(StringComparer.Ordinal))
            || entity.FindPrimaryKey()?.Properties is not [var key] || key.Name != "Id"
            || entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.GetComplexProperties().Any()
            || entity.GetSkipNavigations().Any() || entity.GetNavigations().Any(navigation => navigation.IsEagerLoaded)
            || entity.GetProperties().Any(property => property.ClrType != (property.Name is "Id" or "TenantId" ? typeof(Guid) : typeof(long))))
            throw new InvalidOperationException("discovery_metadata_model_shape_drift");
        var tenant = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model).SingleOrDefault(table =>
            table.TableName == source.Name && (table.Schema ?? source.Schema) == source.Schema);
        if (tenant is not { AllowsNullTenant: false, ParentJoin: null, TenantIdColumn: not null })
            throw new InvalidOperationException("discovery_metadata_model_tenancy_drift");
        return source;
    }

    private static Source Map(ExploreDbContext context, Type type, string[] properties)
    {
        var entity = context.Model.FindEntityType(type) ?? throw new InvalidOperationException("discovery_metadata_model_missing");
        string name = entity.GetTableName() ?? throw new InvalidOperationException("discovery_metadata_table_missing");
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema ?? entity.GetSchema() ?? "public";
        var mapping = StoreObjectIdentifier.Table(name, entity.GetSchema());
        return new(type, schema, name, context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, schema), properties.ToDictionary(property => property,
            property => entity.FindProperty(property)?.GetColumnName(mapping) ?? throw new InvalidOperationException("discovery_metadata_column_missing")));
    }

    private sealed record Source(Type Type, string Schema, string Name, string Table, Dictionary<string, string> Columns);

    private static DbCommand Command(ExploreDbContext context, string sql)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? 30;
        command.CommandText = sql;
        return command;
    }

    private static async Task<object?> ScalarAsync(ExploreDbContext context, string sql, CancellationToken token)
    {
        await using var command = Command(context, sql);
        return await command.ExecuteScalarAsync(token);
    }

    private static async Task ExecuteAsync(ExploreDbContext context, string sql, CancellationToken token)
    {
        await using var command = Command(context, sql);
        await command.ExecuteNonQueryAsync(token);
    }

    // Enrollment names are literal catalog values, not identifiers to parse or case-fold.
    private static string RoleOid(string login) => $"(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})";

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
