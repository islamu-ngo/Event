using Explore.Domain;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Explore.Persistence.Security;

/// <summary>
/// SQL compilation shared by the Event read projections. Source inventories, visibility,
/// ACLs and fingerprint namespaces remain with each owner; this helper grants no authority.
/// </summary>
internal static class EventReadProjectionSql
{
    /// <summary>
    /// Rejects source authority beyond direct fixed Runtime reference reads. Reference grants may
    /// be absent during installation; each projection retains its own complete private-source inventory.
    /// </summary>
    internal static string RuntimeCallerPrivilegeGuard(ExploreDbContext context, HumanPrincipalSourceSql names,
        IReadOnlyCollection<Type> sources, PostgresNativeQueueEnrollment enrollment, string diagnosticPrefix,
        bool allowLookupReferences = true)
    {
        string runtime = Text(enrollment.RuntimeLogin);
        string relations = string.Join(",", sources.Select(type => Text(names.Table(type)) + "::regclass"));
        var references = new List<string>();
        foreach (var type in allowLookupReferences ? sources : [])
        {
            PostgresLookupCatalogueReadPermissions.TryGetRuntimeReadColumns(context, type, out var columns);
            if (columns.Count != 0)
                references.Add($"(t.oid = {Text(names.Table(type))}::regclass AND a.attname = ANY(ARRAY[{string.Join(",", columns.Select(Text))}]::text[]))");
        }
        string declaredReference = references.Count == 0 ? "false" : string.Join(" OR ", references);
        string referenceRead = $"""
            r.rolname = {runtime} AND a.attnum > 0 AND ({declaredReference})
            AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                WHERE acl.grantee = pg_catalog.to_regrole({runtime})::oid
                    AND acl.privilege_type = 'SELECT' AND NOT acl.is_grantable)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                WHERE acl.grantee = 0 AND acl.privilege_type = 'SELECT')
            """;
        referenceRead = $"({referenceRead}) OR ({PostgresTenantApiKeyStoragePermissions.RuntimeParentReadPredicate(context, enrollment, "r.oid", "t.oid", "a.attname", "'SELECT'")})";
        if (sources.Contains(typeof(Category)))
            referenceRead = $"({referenceRead}) OR ({PostgresCategoryReadPermissions.RuntimeReadPredicate(context,
                typeof(Category), enrollment.RuntimeLogin, "r.oid", "t.oid", "a.attname", "'SELECT'")})";
        return $"""
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE pg_catalog.pg_has_role({runtime},r.oid,'MEMBER')
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                        OR pg_catalog.has_schema_privilege(r.oid,pg_catalog.to_regnamespace({Text(names.Schema)}),'CREATE')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r
                    WHERE t.oid IN ({relations}) AND pg_catalog.pg_has_role({runtime},r.oid,'MEMBER')
                        AND (r.oid = t.relowner
                            OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                            OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                WHERE a.attrelid = t.oid AND a.attnum <> 0 AND NOT a.attisdropped
                                    AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,'INSERT,UPDATE,REFERENCES,SELECT WITH GRANT OPTION')
                                        OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,'SELECT')
                                            AND NOT ({referenceRead}))))))
            THEN RAISE EXCEPTION '{diagnosticPrefix}_runtime_source_privilege' USING ERRCODE = '42501'; END IF;
            """;
    }

    internal static string TenantPredicate(ExploreDbContext context, HumanPrincipalSourceSql names,
        TenantTableMetadata? tenant, string table, string aliasPrefix,
        Func<Type, string, string, string> registerColumn) =>
        tenant is null ? "true"
            : tenant.ParentJoin is { } parent
                ? ParentPredicate(context, names, parent, table, aliasPrefix, 0, registerColumn)
                : TenantMatch(names.Quote(tenant.TenantIdColumn!), tenant.AllowsNullTenant);

    private static string TenantMatch(string column, bool nullable)
    {
        string match = column + " = NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid";
        return nullable ? $"({column} IS NULL OR {match})" : match;
    }

    private static string ParentPredicate(ExploreDbContext context, HumanPrincipalSourceSql names,
        TenantParentJoinMetadata parent, string child, string aliasPrefix, int depth,
        Func<Type, string, string, string> registerColumn)
    {
        string alias = aliasPrefix + depth;
        var entity = context.Model.GetEntityTypes().Single(entity => entity.GetTableName() == parent.ParentTableName);
        string Register(string name)
        {
            var property = entity.GetProperties().Single(property => names.ColumnName(entity.ClrType, property.Name) == name);
            return registerColumn(entity.ClrType, alias, property.Name);
        }
        // Register at every level before recursion: each owner derives its grants and
        // catalog proof from these callbacks, including otherwise unprojected parents.
        string key = Register(parent.ParentKeyColumn);
        string tenantColumn = parent.ParentTenantIdColumn is { } column ? Register(column) : "";
        string predicate = parent.ParentJoin is { } next
            ? ParentPredicate(context, names, next, alias, aliasPrefix, depth + 1, registerColumn)
            : TenantMatch(tenantColumn, parent.ParentAllowsNullTenant);
        return $"EXISTS (SELECT 1 FROM {names.Name(parent.ParentTableName)} {alias} WHERE {key} = {child}.{names.Quote(parent.ForeignKeyColumn)} AND {predicate})";
    }

    internal static string ReadPolicyGuard(string table, string policy, string ownerRole,
        string fingerprint, string diagnosticPrefix) =>
        string.Join("\n", new[] { false, true }.Select(permissive =>
        {
            string name = permissive ? policy + "_allow" : policy;
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = {Text(table)}::regclass AND p.polname = {Text(name)}
                        AND p.polcmd = 'r' AND p.polpermissive = {Boolean(permissive)}
                        AND p.polroles = ARRAY[pg_catalog.to_regrole('{ownerRole}')::oid]
                        AND p.polqual IS NOT NULL AND p.polwithcheck IS NULL
                        AND pg_catalog.obj_description(p.oid,'pg_policy') =
                            {Text(fingerprint)} || pg_catalog.md5(p.polqual::text))
                THEN RAISE EXCEPTION '{diagnosticPrefix}_policy_drift' USING ERRCODE = '55000'; END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = {Text(table)}::regclass
                        AND p.polname NOT IN ('tenant_isolation','{policy}','{policy}_allow')
                        AND EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                            WHERE role_id = 0 OR pg_catalog.pg_has_role('{ownerRole}',role_id,'USAGE')))
                THEN RAISE EXCEPTION '{diagnosticPrefix}_unexpected_policy' USING ERRCODE = '55000'; END IF;
                """;
        }));

    /// <summary>
    /// Public projection sources must be ordinary columns, never computed columns.
    /// The private readers retain their distinct computed-expression attestation.
    /// </summary>
    internal static string PublicSourceGuard(ExploreDbContext context, HumanPrincipalSourceSql names,
        IReadOnlyDictionary<Type, HashSet<string>> sources, string migratorLogin, string diagnosticPrefix)
    {
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(names.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(migratorLogin)})::oid)
            THEN RAISE EXCEPTION '{diagnosticPrefix}_namespace_drift' USING ERRCODE = '55000'; END IF;
            """ + string.Join("\n", sources.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Key)!;
            var declared = context.GetService<IDesignTimeModel>().Model.FindEntityType(source.Key)!;
            var tenant = tenantTables.SingleOrDefault(table => table.TableName == entity.GetTableName());
            string table = Text(names.Table(source.Key));
            string columns = string.Join(" OR ", source.Value.Select(name =>
            {
                var property = entity.FindProperty(name)!;
                string collation = property.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value is true
                    || property.FindAnnotation(LocationUnicodePropertyExtensions.AnnotationName)?.Value is true ? "C" : "default";
                string identity = NpgsqlPropertyExtensions.GetValueGenerationStrategy(declared.FindProperty(name)!) switch
                {
                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn => "d",
                    NpgsqlValueGenerationStrategy.IdentityAlwaysColumn => "a",
                    _ => ""
                };
                return $"""
                    NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = {table}::regclass
                        AND a.attname = {Text(names.ColumnName(source.Key, name))} AND a.attnum > 0 AND NOT a.attisdropped
                        AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                        AND a.attnotnull = {Boolean(!property.IsNullable)} AND a.attgenerated = '' AND a.attidentity = {Text(identity)}
                        AND (a.attcollation = 0 OR a.attcollation = pg_catalog.to_regcollation({Text("pg_catalog." + names.Quote(collation))})))
                    """;
            }));
            string primary = string.Join(",", entity.FindPrimaryKey()!.Properties.Select(property =>
                Text(names.ColumnName(source.Key, property.Name))));
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relkind = 'r' AND t.relpersistence = 'p' AND t.relowner = pg_catalog.to_regrole({Text(migratorLogin)})::oid
                    AND t.relrowsecurity = {Boolean(tenant is not null)} AND t.relforcerowsecurity = {Boolean(tenant is not null)})
                    OR {columns}
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = {table}::regclass)
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid = {table}::regclass OR i.inhparent = {table}::regclass)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indrelid = {table}::regclass
                        AND i.indisprimary AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate
                        AND i.indexprs IS NULL AND i.indpred IS NULL
                        AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id ORDER BY k.ord)
                            = ARRAY[{primary}]::name[])
                THEN RAISE EXCEPTION '{diagnosticPrefix}_source_catalog_drift' USING ERRCODE = '55000'; END IF;
                {(tenant is null ? "" : $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {table}::regclass
                        AND p.polname = 'tenant_isolation' AND p.polcmd = '*' AND p.polpermissive
                        AND p.polroles = ARRAY[0::oid] AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                        AND pg_catalog.obj_description(p.oid,'pg_policy') =
                            {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, names.Schema))}
                            || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                    THEN RAISE EXCEPTION '{diagnosticPrefix}_tenant_policy_drift' USING ERRCODE = '55000'; END IF;
                    """)}
                """;
        }));
    }

    private static string Boolean(bool value) => value ? "true" : "false";
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
