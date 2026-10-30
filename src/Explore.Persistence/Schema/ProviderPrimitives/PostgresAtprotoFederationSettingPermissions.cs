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

/// <summary>Owns fixed federation-table privileges and policy attestation, never federation business decisions.</summary>
public static class PostgresAtprotoFederationSettingPermissions
{
    private const string GlobalReadPolicy = "atproto_federation_global_read";
    private static readonly string[] InstanceProperties =
    [
        nameof(AtprotoFederationInstanceSetting.Id), nameof(AtprotoFederationInstanceSetting.SettingKey),
        nameof(AtprotoFederationInstanceSetting.Value), nameof(AtprotoFederationInstanceSetting.IsLocked),
        nameof(AtprotoFederationInstanceSetting.CreatedAt), nameof(AtprotoFederationInstanceSetting.CreatedBy),
        nameof(AtprotoFederationInstanceSetting.UpdatedAt), nameof(AtprotoFederationInstanceSetting.UpdatedBy)
    ];
    private static readonly string[] UpdateProperties = ["Value", "IsLocked", "UpdatedAt", "UpdatedBy"];

    internal static void ValidateModel(ExploreDbContext context)
    {
        foreach (var source in Sources(context))
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            string[] expected = source.Tenant ? [.. InstanceProperties, "TenantId"] : InstanceProperties;
            if (!entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(expected.Order(StringComparer.Ordinal))
                || entity.FindPrimaryKey()?.Properties is not [var key] || key.Name != "Id"
                || entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.GetComplexProperties().Any()
                || entity.GetSkipNavigations().Any() || entity.GetNavigations().Any(navigation => navigation.IsEagerLoaded)
                || entity.GetProperties().Any(property => property.ClrType != (property.Name switch
                {
                    "Id" or "TenantId" => typeof(Guid),
                    "CreatedBy" or "UpdatedBy" => typeof(Guid?),
                    "CreatedAt" => typeof(DateTime),
                    "UpdatedAt" => typeof(DateTime?),
                    "IsLocked" => typeof(bool),
                    _ => typeof(string)
                })))
                throw new InvalidOperationException("federation_settings_model_shape_drift");
            var tenantTable = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
                .SingleOrDefault(table => table.TableName == source.Name && (table.Schema ?? source.Schema) == source.Schema);
            if (source.Tenant
                ? tenantTable is not { AllowsNullTenant: false, ParentJoin: null, TenantIdColumn: not null }
                : tenantTable is not null)
                throw new InvalidOperationException("federation_settings_model_tenancy_drift");
        }
    }

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        ValidateModel(context);
        var sources = Sources(context);
        var tenant = sources.Single(source => source.Tenant);
        var names = context.GetService<ISqlGenerationHelper>();
        string complete = ManifestPredicate(context, enrollment, complete: true);
        string fresh = FreshPredicate(context, enrollment);
        string policy = names.DelimitIdentifier(GlobalReadPolicy);
        string grants = string.Join('\n', Grants(sources, enrollment, names));
        string preflightSql = $"""
            SELECT ({ManifestPredicate(context, enrollment, complete: false)})
                AND (({fresh}) OR ({complete})) AS "Value"
            """;
        string freshSql = $"SELECT ({fresh}) AS \"Value\"";
        string completeSql = $"SELECT ({complete}) AS \"Value\"";
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        if (!await ReadBooleanProofAsync(context, """
                SELECT session_user=@migrator AND current_user=session_user
                    AND pg_catalog.current_setting('role') IN ('none',session_user::text) AS "Value"
                """, cancellationToken, new NpgsqlParameter("migrator", enrollment.MigratorLogin)))
            throw new UnauthorizedAccessException("federation_settings_installer_wrong_identity");
        if (!await ReadBooleanProofAsync(context, preflightSql, cancellationToken))
            throw new InvalidOperationException("federation_settings_permissions_drift");
        if (await ReadBooleanProofAsync(context, freshSql, cancellationToken))
        {
            var owner = context.Database.CurrentTransaction!;
            string savepoint = "federation_permissions_" + Guid.CreateVersion7().ToString("N");
            await owner.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                string ddl = $"""
                    {grants}
                    CREATE POLICY {policy} ON {tenant.Table} FOR SELECT
                        TO {names.DelimitIdentifier(enrollment.GlobalLogin)} USING (true);
                    """;
                await context.Database.ExecuteSqlRawAsync(ddl, cancellationToken);
                string expressionStamp = await ReadGlobalPolicyExpressionStampAsync(context, tenant, cancellationToken);
                string stamp = $"COMMENT ON POLICY {policy} ON {tenant.Table} IS {Text(PolicyStamp(tenant, enrollment.GlobalLogin) + expressionStamp)}";
                await context.Database.ExecuteSqlRawAsync(stamp, cancellationToken);
                if (!await ReadBooleanProofAsync(context, completeSql, cancellationToken))
                    throw new InvalidOperationException("federation_settings_permissions_drift");
            }
            catch
            {
                await owner.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                throw;
            }
            finally
            {
                await owner.ReleaseSavepointAsync(savepoint, CancellationToken.None);
            }
        }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task RequireReadAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        bool globalOnly, CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException("federation_settings_enrollment_required");
        enrollment.Validate();
        string identity = globalOnly ? $"session_user={Text(enrollment.GlobalLogin)}"
            : $"session_user IN ({Text(enrollment.RuntimeLogin)},{Text(enrollment.GlobalLogin)})";
        string sql = $"""
            SELECT ({identity}) AND current_user=session_user
                AND pg_catalog.current_setting('role') IN ('none',session_user::text)
                AND pg_catalog.current_setting('session_replication_role')='origin'
                AND COALESCE(pg_catalog.current_setting('app.current_tenant_id',true),'')=@tenant
                AND ({ManifestPredicate(context, enrollment, complete: true)}) AS "Value"
            """;
        if (!await ReadBooleanProofAsync(context, sql, cancellationToken,
                new NpgsqlParameter("tenant", context.TenantFilterTenantId?.ToString() ?? string.Empty)))
            throw new UnauthorizedAccessException("federation_settings_read_not_ready");
        await using var readiness = PostgresTenantRuntimeReadiness.CreateCommand(
            context.Database.GetDbConnection(), context, enrollment.GlobalLogin);
        readiness.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        PostgresTenantRuntimeReadiness.RequireReady(await readiness.ExecuteScalarAsync(cancellationToken));
    }

    // Catalog proofs must not replay or replace an explicit caller-owned transaction through EF's query retry strategy.
    private static async Task<bool> ReadBooleanProofAsync(ExploreDbContext context, string proof,
        CancellationToken cancellationToken, params NpgsqlParameter[] parameters)
    {
        await using var command = CreateProofCommand(context, proof);
        foreach (var parameter in parameters) command.Parameters.Add(parameter);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<string> ReadGlobalPolicyExpressionStampAsync(ExploreDbContext context,
        Source tenant, CancellationToken cancellationToken)
    {
        await using var command = CreateProofCommand(context, """
            SELECT pg_catalog.md5(p.polqual::text) FROM pg_catalog.pg_policy p
            WHERE p.polrelid=pg_catalog.to_regclass(@table) AND p.polname=@policy
            """);
        command.Parameters.Add(new NpgsqlParameter("table", tenant.Table));
        command.Parameters.Add(new NpgsqlParameter("policy", GlobalReadPolicy));
        return await command.ExecuteScalarAsync(cancellationToken) as string
            ?? throw new InvalidOperationException("federation_settings_policy_stamp_missing");
    }

    private static DbCommand CreateProofCommand(ExploreDbContext context, string proof)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = proof;
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? 30;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }

    /// <summary>Closed exception for the existing readiness query's p/c/n aliases, only for the configured Global login.</summary>
    internal static string GlobalReadPolicyException(ExploreDbContext context, string enrolledGlobalLogin)
    {
        var tenant = Sources(context).Single(source => source.Tenant);
        return $"""
            session_user={Text(enrolledGlobalLogin)} AND current_user=session_user
            AND n.nspname={Text(tenant.Schema)} AND c.relname={Text(tenant.Name)}
            AND ({GlobalPolicyPredicate(tenant, enrolledGlobalLogin)})
            """;
    }

    /// <summary>Existing fixed tenant-source rights, without composing a caller or catalogue proof.</summary>
    internal static (IReadOnlyList<string> Read, IReadOnlyList<string> Insert, IReadOnlyList<string> Update) GlobalTenantColumns(
        ExploreDbContext context)
    {
        var source = Sources(context).Single(value => value.Tenant);
        return (Array.AsReadOnly(source.Columns.Values.ToArray()),
            Array.AsReadOnly(source.Columns.Values.ToArray()),
            Array.AsReadOnly(UpdateProperties.Select(property => source.Columns[property]).ToArray()));
    }

    internal static string GlobalTenantPolicy(ExploreDbContext context, string login) =>
        GlobalPolicyPredicate(Sources(context).Single(value => value.Tenant), login);

    internal static string CompleteGlobalCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        ValidateModel(context);
        return ManifestPredicate(context, enrollment, complete: true);
    }

    internal static IReadOnlyList<string> GlobalCatalogueRelations(ExploreDbContext context) =>
        Sources(context).Select(source => source.Table).ToArray();

    private static string ManifestPredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        var sources = Sources(context);
        var instance = sources.Single(source => !source.Tenant);
        var tenant = sources.Single(source => source.Tenant);
        string tables = "ARRAY[" + string.Join(',', sources.Select(source => Text(source.Table))) + "]::text[]";
        string schemas = "ARRAY[" + string.Join(',', sources.Select(source => Text(source.Schema)).Distinct()) + "]::text[]";
        string owner = Text(enrollment.MigratorLogin);
        string runtime = Text(enrollment.RuntimeLogin);
        string global = Text(enrollment.GlobalLogin);
        string exactColumns = string.Join(" AND ", sources.Select(source => $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attnum>0 AND NOT a.attisdropped
                    AND NOT (a.attname=ANY(ARRAY[{string.Join(',', source.Columns.Values.Select(Text))}]::text[])))
            """));
        string required = complete ? $"""
            AND NOT EXISTS (SELECT 1 FROM expected e WHERE NOT EXISTS (
                SELECT 1 FROM actual a WHERE a.relation=e.relation AND a.column_name IS NOT DISTINCT FROM e.column_name
                    AND a.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=e.login) AND a.privilege_type=e.privilege_type
                    AND NOT a.is_grantable AND a.grantor=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={owner})))
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                WHERE p.polrelid=pg_catalog.to_regclass({Text(tenant.Table)}) AND ({GlobalPolicyPredicate(tenant, enrollment.GlobalLogin)}))
            AND pg_catalog.has_schema_privilege({runtime},{Text(tenant.Schema)},'USAGE')
            AND pg_catalog.has_schema_privilege({global},{Text(tenant.Schema)},'USAGE')
            """ : "";
        return $"""
            (WITH expected(relation,column_name,login,privilege_type) AS ({ExpectedGrants(sources, enrollment)}),
            actual AS ({ActualGrants(tables, owner)})
            SELECT
                (SELECT count(*)=2 FROM pg_catalog.pg_roles WHERE rolname IN ({runtime},{global}) AND rolcanlogin)
                AND ({RetiredGovernanceAbsentPredicate(context)})
                AND NOT EXISTS (SELECT 1 FROM unnest({tables}) relation
                    LEFT JOIN pg_catalog.pg_class c ON c.oid=pg_catalog.to_regclass(relation)
                    WHERE c.oid IS NULL OR c.relkind<>'r' OR c.relpersistence<>'p'
                        OR c.relowner<>(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={owner}))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class c
                    WHERE c.oid=pg_catalog.to_regclass({Text(instance.Table)}) AND (c.relrowsecurity OR c.relforcerowsecurity))
                AND {exactColumns}
                AND NOT EXISTS (SELECT 1 FROM actual a WHERE a.is_grantable
                    OR a.grantor<>(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={owner})
                    OR NOT EXISTS (SELECT 1 FROM expected e WHERE e.relation=a.relation
                        AND e.column_name IS NOT DISTINCT FROM a.column_name
                        AND (SELECT oid FROM pg_catalog.pg_roles WHERE rolname=e.login)=a.grantee AND e.privilege_type=a.privilege_type))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                    WHERE (pg_catalog.pg_has_role({runtime},r.oid,'MEMBER') OR pg_catalog.pg_has_role({global},r.oid,'MEMBER'))
                        AND (r.oid=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={owner})
                            OR r.rolsuper OR r.rolbypassrls OR r.rolcreaterole OR r.rolcreatedb OR r.rolreplication
                            OR EXISTS (SELECT 1 FROM unnest({schemas}) schema_name WHERE pg_catalog.has_schema_privilege(r.oid,schema_name,'CREATE'))))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members
                    WHERE roleid IN ((SELECT oid FROM pg_catalog.pg_roles WHERE rolname={runtime}),(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={global})))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                    WHERE t.tgrelid=ANY(SELECT pg_catalog.to_regclass(relation) FROM unnest({tables}) relation) AND NOT t.tgisinternal)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r
                    WHERE r.ev_class=ANY(SELECT pg_catalog.to_regclass(relation) FROM unnest({tables}) relation))
                AND ({TenantPolicyPredicate(context, tenant)})
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid=ANY(SELECT pg_catalog.to_regclass(relation) FROM unnest({tables}) relation)
                        AND NOT (p.polrelid=pg_catalog.to_regclass({Text(tenant.Table)})
                            AND (p.polname='tenant_isolation' OR ({GlobalPolicyPredicate(tenant, enrollment.GlobalLogin)})
                                OR ({PostgresManagedTenantWorkerCreationPermissions.SourcePolicyPredicate(context, typeof(AtprotoFederationTenantSetting), enrollment.GlobalLogin)}))))
                AND ({PostgresManagedTenantWorkerCreationPermissions.OptionalGlobalCataloguePredicate(context, enrollment)})
                {required})
            """;
    }

    /// <summary>Rejects obsolete deployment authority without attempting operator-owned decommissioning.</summary>
    private static string RetiredGovernanceAbsentPredicate(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(TenantSetting))
            ?? throw new InvalidOperationException("federation_settings_retirement_mapping_missing");
        string table = entity.GetTableName()
            ?? throw new InvalidOperationException("federation_settings_retirement_mapping_missing");
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        return $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                WHERE rolname IN ('event_atproto_governance_owner', 'event_atproto_governance_migrator', 'event_atproto_governance_runtime'))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc p
                JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
                WHERE n.nspname={Text(schema)} AND p.proname={Text(table + "_atproto_governance")})
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                JOIN pg_catalog.pg_class c ON c.oid=p.polrelid
                JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname={Text(schema)} AND c.relname={Text(table)} AND p.polname='atproto_governance_overrides')
            """;
    }

    private static string FreshPredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sources = Sources(context);
        string tables = "ARRAY[" + string.Join(',', sources.Select(source => Text(source.Table))) + "]::text[]";
        return $"""
            NOT EXISTS ({ActualGrants(tables, Text(enrollment.MigratorLogin))})
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                WHERE p.polrelid=ANY(SELECT pg_catalog.to_regclass(relation) FROM unnest({tables}) relation)
                    AND p.polname={Text(GlobalReadPolicy)})
            """;
    }

    private static string ActualGrants(string tables, string owner) => $"""
        SELECT relation, NULL::text AS column_name, acl.grantee, acl.grantor, acl.privilege_type, acl.is_grantable
        FROM unnest({tables}) relation JOIN pg_catalog.pg_class c ON c.oid=pg_catalog.to_regclass(relation)
        CROSS JOIN LATERAL pg_catalog.aclexplode(COALESCE(c.relacl,pg_catalog.acldefault('r',c.relowner))) acl
        WHERE acl.grantee<>(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={owner})
        UNION ALL
        SELECT relation, a.attname::text, acl.grantee, acl.grantor, acl.privilege_type, acl.is_grantable
        FROM unnest({tables}) relation JOIN pg_catalog.pg_attribute a ON a.attrelid=pg_catalog.to_regclass(relation)
        CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
        WHERE a.attnum<>0 AND NOT a.attisdropped AND acl.grantee<>(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={owner})
        """;

    private static string ExpectedGrants(Source[] sources, PostgresNativeQueueEnrollment enrollment)
    {
        var values = new List<string>();
        foreach (var source in sources)
            foreach (string login in new[] { enrollment.RuntimeLogin, enrollment.GlobalLogin })
            {
                foreach (string column in source.Columns.Values) Add(column, "SELECT");
                if (source.Tenant || login == enrollment.GlobalLogin)
                {
                    foreach (string column in source.Columns.Values) Add(column, "INSERT");
                    foreach (string property in UpdateProperties) Add(source.Columns[property], "UPDATE");
                }
                if (source.Tenant) Add(null, "DELETE");
                void Add(string? column, string privilege) => values.Add(
                    $"({Text(source.Table)},{(column is null ? "NULL::text" : Text(column))},{Text(login)},{Text(privilege)})");
            }
        return "VALUES " + string.Join(',', values);
    }

    private static IEnumerable<string> Grants(Source[] sources, PostgresNativeQueueEnrollment enrollment, ISqlGenerationHelper names)
    {
        foreach (var source in sources)
            foreach (string login in new[] { enrollment.RuntimeLogin, enrollment.GlobalLogin })
            {
                string role = names.DelimitIdentifier(login);
                string columns = string.Join(',', source.Columns.Values.Select(names.DelimitIdentifier));
                yield return $"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {role};";
                yield return $"GRANT SELECT({columns}) ON {source.Table} TO {role};";
                if (source.Tenant || login == enrollment.GlobalLogin)
                {
                    yield return $"GRANT INSERT({columns}) ON {source.Table} TO {role};";
                    yield return $"GRANT UPDATE({string.Join(',', UpdateProperties.Select(property => names.DelimitIdentifier(source.Columns[property])))}) ON {source.Table} TO {role};";
                }
                if (source.Tenant) yield return $"GRANT DELETE ON {source.Table} TO {role};";
            }
    }

    private static string TenantPolicyPredicate(ExploreDbContext context, Source tenant)
    {
        var table = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .Single(value => value.TableName == tenant.Name && (value.Schema ?? tenant.Schema) == tenant.Schema);
        string prefix = PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(table, tenant.Schema);
        return $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_policy p ON p.polrelid=c.oid
                WHERE c.oid=pg_catalog.to_regclass({Text(tenant.Table)}) AND c.relrowsecurity AND c.relforcerowsecurity
                    AND p.polname='tenant_isolation' AND p.polcmd='*' AND p.polroles=ARRAY[0::oid] AND p.polpermissive
                    AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                    AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(prefix)}
                        || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
            """;
    }

    private static string GlobalPolicyPredicate(Source tenant, string global) => $"""
        p.polname={Text(GlobalReadPolicy)} AND p.polrelid=pg_catalog.to_regclass({Text(tenant.Table)})
        AND p.polcmd='r' AND p.polroles=ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(global)})] AND p.polpermissive
        AND p.polqual IS NOT NULL AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)='true' AND p.polwithcheck IS NULL
        AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PolicyStamp(tenant, global))} || pg_catalog.md5(p.polqual::text)
        """;

    private static string PolicyStamp(Source tenant, string global) => "event-federation-global-read:v1:"
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenant.Schema}\n{tenant.Name}\n{global}\nSELECT"))) + ":";

    private static Source[] Sources(ExploreDbContext context) =>
    [
        Map(context, typeof(AtprotoFederationInstanceSetting), false),
        Map(context, typeof(AtprotoFederationTenantSetting), true)
    ];

    private static Source Map(ExploreDbContext context, Type type, bool tenant)
    {
        var entity = context.Model.FindEntityType(type) ?? throw new InvalidOperationException("federation_settings_model_missing");
        string name = entity.GetTableName() ?? throw new InvalidOperationException("federation_settings_table_missing");
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        var mapping = StoreObjectIdentifier.Table(name, entity.GetSchema());
        string[] properties = tenant ? [.. InstanceProperties, "TenantId"] : InstanceProperties;
        var columns = properties.ToDictionary(property => property, property =>
            entity.FindProperty(property)?.GetColumnName(mapping) ?? throw new InvalidOperationException("federation_settings_column_missing"));
        return new(type, tenant, schema, name, context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, schema), columns);
    }

    private sealed record Source(Type Type, bool Tenant, string Schema, string Name, string Table, Dictionary<string, string> Columns);

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
