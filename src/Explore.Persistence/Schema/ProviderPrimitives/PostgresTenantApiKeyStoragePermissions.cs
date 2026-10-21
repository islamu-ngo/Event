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
/// Fixed tenant-key storage permissions. Issuance, disclosure, management and revocation remain application workflows.
/// Runtime can insert a new hash but cannot select hashes or usage IP addresses.
/// </summary>
public static class PostgresTenantApiKeyStoragePermissions
{
    private const string Policy = "tenant_key_runtime_storage";
    private const string Drift = "tenant_api_key_storage_permissions_not_ready";
    private static readonly string[] KeyColumns =
    [
        nameof(ExternalApiKey.Id), nameof(ExternalApiKey.TenantId), nameof(ExternalApiKey.Name),
        nameof(ExternalApiKey.Description), nameof(ExternalApiKey.KeyId), nameof(ExternalApiKey.SecretHash),
        nameof(ExternalApiKey.Scopes), nameof(ExternalApiKey.ExternalApiKeyOwnerTypeId), nameof(ExternalApiKey.OwnerId),
        nameof(ExternalApiKey.ExternalApiKeyStatusId), nameof(ExternalApiKey.ExternalApiKeyCreditPeriodId),
        nameof(ExternalApiKey.CreditLimit), nameof(ExternalApiKey.MaxRolloverCredits), nameof(ExternalApiKey.ExpiresAt),
        nameof(ExternalApiKey.LastUsedAt), nameof(ExternalApiKey.LastUsedIp), nameof(ExternalApiKey.CreatedAt),
        nameof(ExternalApiKey.CreatedBy), nameof(ExternalApiKey.UpdatedAt), nameof(ExternalApiKey.UpdatedBy)
    ];
    private static readonly string[] MetadataColumns =
    [
        nameof(ExternalApiKey.Id), nameof(ExternalApiKey.TenantId), nameof(ExternalApiKey.Name),
        nameof(ExternalApiKey.Description), nameof(ExternalApiKey.KeyId), nameof(ExternalApiKey.Scopes),
        nameof(ExternalApiKey.ExternalApiKeyOwnerTypeId), nameof(ExternalApiKey.OwnerId),
        nameof(ExternalApiKey.ExternalApiKeyStatusId), nameof(ExternalApiKey.ExternalApiKeyCreditPeriodId),
        nameof(ExternalApiKey.CreditLimit), nameof(ExternalApiKey.MaxRolloverCredits), nameof(ExternalApiKey.ExpiresAt),
        nameof(ExternalApiKey.LastUsedAt), nameof(ExternalApiKey.CreatedAt), nameof(ExternalApiKey.CreatedBy),
        nameof(ExternalApiKey.UpdatedAt), nameof(ExternalApiKey.UpdatedBy)
    ];
    private static readonly string[] UpdateColumns =
    [
        nameof(ExternalApiKey.Id), nameof(ExternalApiKey.Name), nameof(ExternalApiKey.Scopes),
        nameof(ExternalApiKey.ExpiresAt), nameof(ExternalApiKey.ExternalApiKeyStatusId),
        nameof(ExternalApiKey.UpdatedAt), nameof(ExternalApiKey.UpdatedBy)
    ];
    private static readonly string[] ReceiptColumns =
    [
        nameof(ExternalApiKeyIssuanceReceipt.Id), nameof(ExternalApiKeyIssuanceReceipt.OperationFingerprint),
        nameof(ExternalApiKeyIssuanceReceipt.InputDigest), nameof(ExternalApiKeyIssuanceReceipt.TenantId),
        nameof(ExternalApiKeyIssuanceReceipt.ExternalApiKeyId), nameof(ExternalApiKeyIssuanceReceipt.CreatedAtUtc)
    ];

    /// <summary>Installs a fresh complete descriptor or verifies it without repairing partial or widened grants.</summary>
    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        Source[] sources = Sources(context);
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
        var connection = RelationalConnectionIdentity.Capture(context);
        var contextId = context.ContextId;
        var physical = transaction.GetDbTransaction();
        int? backend = connection.BackendProcessId;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (interrupted || context.ContextId != contextId || !connection.Matches(context)
                || connection.State != ConnectionState.Open || connection.BackendProcessId != backend
                || !ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction())
                || !connection.Owns(physical))
                throw new UnauthorizedAccessException(Drift);
        }
        connection.StateChange += Changed;
        string savepoint = "tenant_key_permissions_" + Guid.CreateVersion7().ToString("N");
        try
        {
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                RequireOriginal();
                if (!await ScalarAsync(context, Identity(enrollment.MigratorLogin), cancellationToken))
                    throw new UnauthorizedAccessException(Drift);
                bool fresh = await ScalarAsync(context, Fresh(sources, enrollment.RuntimeLogin), cancellationToken);
                RequireOriginal();
                if (!await ScalarAsync(context, Manifest(context, sources, enrollment, complete: !fresh), cancellationToken))
                    throw new UnauthorizedAccessException(Drift);
                RequireOriginal();
                if (fresh)
                {
                    var names = context.GetService<ISqlGenerationHelper>();
                    string runtime = names.DelimitIdentifier(enrollment.RuntimeLogin);
                    foreach (Source source in sources)
                    {
                        await context.Database.ExecuteSqlRawAsync($"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {runtime}", cancellationToken);
                        foreach (var grant in source.Grants.GroupBy(grant => grant.Privilege))
                            await context.Database.ExecuteSqlRawAsync($"GRANT {grant.Key} ({string.Join(',', grant.Select(item => names.DelimitIdentifier(item.Column)))}) ON {source.Table} TO {runtime}", cancellationToken);
                        if (source.TenantColumn is { } tenantColumn)
                        {
                            string condition = names.DelimitIdentifier(tenantColumn)
                                + " = NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid";
                            await context.Database.ExecuteSqlRawAsync($"CREATE POLICY {names.DelimitIdentifier(Policy)} ON {source.Table} AS RESTRICTIVE FOR ALL TO {runtime} USING ({condition}) WITH CHECK ({condition})", cancellationToken);
                        }
                        RequireOriginal();
                    }
                    if (!await ScalarAsync(context, Manifest(context, sources, enrollment, complete: true), cancellationToken))
                        throw new UnauthorizedAccessException(Drift);
                }
                RequireOriginal();
            }
            catch
            {
                await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                throw;
            }
            finally { await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None); }
            if (owned is not null) await owned.CommitAsync(cancellationToken);
        }
        finally { connection.StateChange -= Changed; }
    }

    /// <summary>Exact declared Runtime column intersection for another owner-local catalogue proof.</summary>
    internal static string RuntimeColumnException(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        string roleOid, string relationOid, string columnName, string privilege)
    {
        Source source = Sources(context).Single(source => source.Type == typeof(ExternalApiKey));
        string expected = string.Join(',', source.Grants.Select(grant => $"({Text(grant.Column)},{Text(grant.Privilege)})"));
        return $"""
            ({roleOid}={Role(enrollment.RuntimeLogin)} AND {relationOid}=pg_catalog.to_regclass({Text(source.Table)})
                AND EXISTS (SELECT 1 FROM (VALUES {expected}) tenant_key_storage(column_name,privilege)
                    WHERE tenant_key_storage.column_name={columnName} AND tenant_key_storage.privilege={privilege})
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_attribute declared_column CROSS JOIN LATERAL pg_catalog.aclexplode(declared_column.attacl) acl
                    WHERE declared_column.attrelid={relationOid} AND declared_column.attname={columnName}
                        AND acl.grantee={Role(enrollment.RuntimeLogin)} AND acl.grantor={Role(enrollment.MigratorLogin)}
                        AND acl.privilege_type={privilege} AND NOT acl.is_grantable))
            """;
    }

    internal static string CompleteManifest(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        Manifest(context, Sources(context), enrollment, complete: true);

    /// <summary>Only the fixed parent existence key, never tenant descriptive data or user information.</summary>
    internal static IReadOnlyList<string> RuntimeParentReadColumns(ExploreDbContext context, Type type) =>
        type == typeof(Tenant)
            ? Sources(context).Single(source => source.Type == typeof(Tenant)).Grants
                .Where(grant => grant.Privilege == "SELECT").Select(grant => grant.Column).ToArray()
            : [];

    internal static string RuntimeParentReadPredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        string roleOid, string relationOid, string columnName, string privilege)
    {
        Source parent = Sources(context).Single(source => source.Type == typeof(Tenant));
        string id = RuntimeParentReadColumns(context, typeof(Tenant)).Single();
        return $"""
            ({roleOid}={Role(enrollment.RuntimeLogin)} AND {relationOid}=pg_catalog.to_regclass({Text(parent.Table)})
                AND {columnName}={Text(id)} AND {privilege}='SELECT'
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_attribute parent_column
                    CROSS JOIN LATERAL pg_catalog.aclexplode(parent_column.attacl) acl
                    WHERE parent_column.attrelid={relationOid} AND parent_column.attnum>0 AND NOT parent_column.attisdropped
                        AND parent_column.attname={columnName} AND acl.grantee={Role(enrollment.RuntimeLogin)}
                        AND acl.grantor={Role(enrollment.MigratorLogin)} AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute parent_column
                    CROSS JOIN LATERAL pg_catalog.aclexplode(parent_column.attacl) acl
                    WHERE parent_column.attrelid={relationOid} AND parent_column.attname={columnName}
                        AND acl.grantee=0 AND acl.privilege_type='SELECT'))
            """;
    }

    private static string Manifest(ExploreDbContext context, Source[] sources, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string runtime = Role(enrollment.RuntimeLogin), migrator = Role(enrollment.MigratorLogin);
        string expected = string.Join(',', sources.SelectMany(source => source.Grants.Select(grant =>
            $"({Text(source.Table)},{Text(grant.Column)},{Text(grant.Privilege)})")));
        string tables = "ARRAY[" + string.Join(',', sources.Select(source => Text(source.Table))) + "]::text[]";
        string required = complete ? string.Join(" AND ", sources.SelectMany(source => source.Grants.Select(grant => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attname={Text(grant.Column)}
                    AND acl.grantee={runtime} AND acl.grantor={migrator} AND acl.privilege_type={Text(grant.Privilege)} AND NOT acl.is_grantable)
            """))) : "true";
        string policies = string.Join(" AND ", sources.Where(source => source.TenantColumn is not null).Select(source => complete
            ? $"EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {PolicyPredicate(source, enrollment.RuntimeLogin)})"
            : $"NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy WHERE polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND polname={Text(Policy)})"));
        return $"""
            (WITH expected(relation,column_name,privilege) AS (VALUES {expected})
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE oid={runtime} AND rolcanlogin)
                AND NOT pg_catalog.pg_has_role({runtime},{Role(enrollment.GlobalLogin)},'MEMBER')
                AND NOT pg_catalog.pg_has_role({Role(enrollment.GlobalLogin)},{runtime},'MEMBER')
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role({runtime},r.oid,'MEMBER')
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                        OR EXISTS (SELECT 1 FROM unnest({tables}) target(relation)
                            JOIN pg_catalog.pg_class c ON c.oid=pg_catalog.to_regclass(target.relation)
                            WHERE c.relowner=r.oid OR pg_catalog.has_schema_privilege(r.oid,c.relnamespace,'CREATE')
                                OR pg_catalog.has_table_privilege(r.oid,c.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                                OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                    CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                                    WHERE a.attrelid=c.oid AND a.attnum<>0 AND NOT a.attisdropped
                                        AND (pg_catalog.has_column_privilege(r.oid,c.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                            OR (pg_catalog.has_column_privilege(r.oid,c.oid,a.attnum,permission.privilege) AND NOT (
                                                r.oid={runtime} AND a.attnum>0
                                                AND EXISTS (SELECT 1 FROM expected e WHERE pg_catalog.to_regclass(e.relation)=c.oid
                                                    AND e.column_name=a.attname AND e.privilege=permission.privilege)
                                                AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                                    WHERE acl.grantee={runtime} AND acl.grantor={migrator}
                                                        AND acl.privilege_type=permission.privilege AND NOT acl.is_grantable))))))))
                AND NOT EXISTS (SELECT 1 FROM unnest({tables}) target(relation)
                    JOIN pg_catalog.pg_attribute a ON a.attrelid=pg_catalog.to_regclass(target.relation)
                    CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=0)
                AND ({string.Join(" AND ", sources.Select(source => SourcePredicate(context, source, enrollment)))})
                AND ({required}) AND ({policies})
                AND NOT EXISTS (SELECT 1 FROM unnest({tables}) target(relation)
                    JOIN pg_catalog.pg_policy p ON p.polrelid=pg_catalog.to_regclass(target.relation)
                    WHERE p.polname NOT IN ('tenant_isolation',{Text(Policy)})
                        AND EXISTS (SELECT 1 FROM unnest(p.polroles) role(oid)
                            WHERE CASE WHEN role.oid=0 THEN true ELSE pg_catalog.pg_has_role({runtime},role.oid,'MEMBER') END)))
            """;
    }

    private static string SourcePredicate(ExploreDbContext context, Source source, PostgresNativeQueueEnrollment enrollment)
    {
        var tenant = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model).SingleOrDefault(table => table.TableName == source.Name
            && (table.Schema ?? source.Schema) == source.Schema);
        if ((source.TenantColumn is null) != (tenant is null))
            throw new InvalidOperationException("tenant_api_key_storage_fixed_inventory_changed");
        string isolation = source.TenantColumn is null
            ? "NOT c.relrowsecurity AND NOT c.relforcerowsecurity"
            : "c.relrowsecurity AND c.relforcerowsecurity";
        // Non-RLS shared sources may retain dormant policies for unrelated owners, never Runtime-reachable policies.
        string dormantPolicies = $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)})
                    AND EXISTS (SELECT 1 FROM unnest(p.polroles) role(oid)
                        WHERE CASE WHEN role.oid=0 THEN true
                            ELSE pg_catalog.pg_has_role({Role(enrollment.RuntimeLogin)},role.oid,'MEMBER') END))
            """;
        string policy = tenant is null
            ? dormantPolicies
            : $"""
                EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)})
                    AND p.polname='tenant_isolation' AND p.polcmd='*' AND p.polpermissive AND p.polroles=ARRAY[0::oid]
                    AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                    AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, source.Schema))}
                        || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                """;
        string shape = string.Join(" AND ", source.Columns.Select(column => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)})
                AND a.attnum>0 AND NOT a.attisdropped AND a.attname={Text(column.Name)}
                AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(column.StoreType)} AND a.attnotnull={(!column.Nullable ? "true" : "false")}
                AND a.attgenerated='' AND a.attidentity='')
            """));
        return $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                WHERE c.oid=pg_catalog.to_regclass({Text(source.Table)}) AND c.relkind='r' AND c.relpersistence='p'
                    AND NOT c.relispartition AND c.relowner={Role(enrollment.MigratorLogin)} AND n.nspowner={Role(enrollment.MigratorLogin)}
                    AND {isolation})
            AND (SELECT count(*)={source.Columns.Length} FROM pg_catalog.pg_attribute
                WHERE attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND attnum>0 AND NOT attisdropped)
            AND ({shape})
            AND ({policy})
            """;
    }

    private static string PolicyPredicate(Source source, string runtime)
    {
        string expression = $"({Text("(")} || pg_catalog.quote_ident({Text(source.TenantColumn ?? throw new InvalidOperationException(Drift))}) || {Text(" = (NULLIF(current_setting('app.current_tenant_id'::text, true), ''::text))::uuid)")})";
        return $"""
            p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(Policy)}
            AND p.polcmd='*' AND NOT p.polpermissive AND p.polroles=ARRAY[{Role(runtime)}]
            AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)={expression}
            AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid)={expression}
            """;
    }

    private static string Fresh(Source[] sources, string runtime) => string.Join(" AND ", sources.Select(source => $"""
        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy WHERE polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND polname={Text(Policy)})
        AND NOT pg_catalog.has_any_column_privilege({Role(runtime)},{Text(source.Table)},'SELECT,INSERT,UPDATE,REFERENCES')
        """));

    private static Source[] Sources(ExploreDbContext context) =>
    [
        Map(context, typeof(ExternalApiKey), KeyColumns, MetadataColumns, KeyColumns, UpdateColumns),
        Map(context, typeof(ExternalApiKeyIssuanceReceipt), ReceiptColumns, ReceiptColumns, ReceiptColumns, []),
        Map(context, typeof(ExternalApiKeyCreditPeriod),
            [nameof(ExternalApiKeyCreditPeriod.Id), nameof(ExternalApiKeyCreditPeriod.MasterCode),
                nameof(ExternalApiKeyCreditPeriod.FullName), nameof(ExternalApiKeyCreditPeriod.Description)],
            [nameof(ExternalApiKeyCreditPeriod.Id)], [], []),
        Map(context, typeof(ExternalApiKeyOwnerTypeLookup),
            [nameof(ExternalApiKeyOwnerTypeLookup.Id), nameof(ExternalApiKeyOwnerTypeLookup.MasterCode),
                nameof(ExternalApiKeyOwnerTypeLookup.FullName), nameof(ExternalApiKeyOwnerTypeLookup.Description)],
            [nameof(ExternalApiKeyOwnerTypeLookup.Id)], [], []),
        Map(context, typeof(ExternalApiKeyStatus),
            [nameof(ExternalApiKeyStatus.Id), nameof(ExternalApiKeyStatus.MasterCode),
                nameof(ExternalApiKeyStatus.FullName), nameof(ExternalApiKeyStatus.Description), nameof(ExternalApiKeyStatus.IsUsable)],
            [nameof(ExternalApiKeyStatus.Id), nameof(ExternalApiKeyStatus.IsUsable)], [], [nameof(ExternalApiKeyStatus.Id)]),
        Map(context, typeof(Tenant),
            [nameof(Tenant.Id), nameof(Tenant.FullName), nameof(Tenant.Slug), nameof(Tenant.Description),
                nameof(Tenant.TenantStatusId), nameof(Tenant.CreatedAt), nameof(Tenant.CreatedBy),
                nameof(Tenant.UpdatedAt), nameof(Tenant.UpdatedBy)], [nameof(Tenant.Id)], [], [])
    ];

    private static Source Map(ExploreDbContext context, Type type, string[] properties, string[] reads, string[] inserts, string[] updates)
    {
        var entity = context.Model.FindEntityType(type) ?? throw new InvalidOperationException(Drift);
        if (!entity.GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(properties)
            || entity.BaseType is not null || entity.GetDerivedTypes().Any())
            throw new InvalidOperationException("tenant_api_key_storage_fixed_inventory_changed");
        var mapped = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        Column[] columns = properties.Select(property => entity.FindProperty(property)!).Select(property =>
            new Column(property.Name, property.GetColumnName(mapped)!, property.GetRelationalTypeMapping().StoreType, property.IsNullable)).ToArray();
        string Name(string property) => columns.Single(column => column.Property == property).Name;
        Grant[] grants = reads.Select(property => new Grant(Name(property), "SELECT"))
            .Concat(inserts.Select(property => new Grant(Name(property), "INSERT")))
            .Concat(updates.Select(property => new Grant(Name(property), "UPDATE"))).ToArray();
        return new(type, schema, mapped.Name, context.GetService<ISqlGenerationHelper>().DelimitIdentifier(mapped.Name, schema),
            entity.FindProperty("TenantId") is null ? null : Name("TenantId"), columns, grants);
    }

    private static string Role(string login) => $"(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})";
    private static string Identity(string login) => $"session_user=current_user AND current_user={Text(login)}::name AND pg_catalog.current_setting('role') IN ('none',session_user::text) AND pg_catalog.current_setting('session_replication_role')='origin'";
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static async Task<bool> ScalarAsync(ExploreDbContext context, string expression, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT COALESCE((" + expression + "),false)";
        return await command.ExecuteScalarAsync(token) is true;
    }
    private sealed record Column(string Property, string Name, string StoreType, bool Nullable);
    private sealed record Grant(string Column, string Privilege);
    private sealed record Source(Type Type, string Schema, string Name, string Table, string? TenantColumn, Column[] Columns, Grant[] Grants);
}
