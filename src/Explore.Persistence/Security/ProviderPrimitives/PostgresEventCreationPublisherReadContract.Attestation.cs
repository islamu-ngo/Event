using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Explore.Persistence.Security;

public static partial class PostgresEventCreationPublisherReadContract
{
    private static string ExactBody(ExploreDbContext context, string body) => $"""
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
            WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context) + "(uuid,uuid)")})
                AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                AND f.proretset AND f.prorettype = 'pg_catalog.record'::regtype
                AND f.proargdefaults IS NULL AND f.pronargdefaults = 0 AND f.provariadic = 0
                AND f.proargnames = ARRAY['p_tenant','p_principal','is_organization','membership_id',
                    'placement_id','publisher_id','publisher_role_id','display_name','event_create_permission_id']::text[]
                AND f.proargmodes = ARRAY['i','i','t','t','t','t','t','t','t']::"char"[]
                AND f.proallargtypes = ARRAY['uuid'::regtype,'uuid'::regtype,'boolean'::regtype,'uuid'::regtype,
                    'uuid'::regtype,'uuid'::regtype,'integer'::regtype,'text'::regtype,'integer'::regtype]::oid[]
                AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(body))})
        THEN RAISE EXCEPTION 'event_creation_publisher_body_drift' USING ERRCODE = '55000'; END IF;
        """;

    private static string CallerPrivilegeGuard(SourceSql sql, PostgresNativeQueueEnrollment enrollment)
    {
        string sources = string.Join(",", sql.Sources.Keys.Select(type => Text(sql.Names.Table(type)) + "::regclass"));
        return string.Join("\n", new[] { enrollment.RuntimeLogin, enrollment.GlobalLogin }.Select(login => $"""
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role({Text(login)},r.oid,'USAGE') OR pg_catalog.pg_has_role({Text(login)},r.oid,'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r
                    WHERE t.oid IN ({sources})
                        AND (pg_catalog.pg_has_role({Text(login)},r.oid,'USAGE') OR pg_catalog.pg_has_role({Text(login)},r.oid,'SET'))
                        AND (r.oid = t.relowner
                            OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                    WHERE (pg_catalog.pg_has_role({Text(login)},r.oid,'USAGE') OR pg_catalog.pg_has_role({Text(login)},r.oid,'SET'))
                        AND pg_catalog.has_schema_privilege(r.oid,pg_catalog.to_regnamespace({Text(sql.Names.Schema)}),'CREATE'))
            THEN RAISE EXCEPTION 'event_creation_publisher_private_source_privilege' USING ERRCODE = '42501'; END IF;
            """));
    }

    private static string PolicyGuard(SourceSql sql, IReadOnlyDictionary<Type, string> policies) =>
        string.Join("\n", policies.SelectMany(pair => new[] { false, true }.Select(permissive =>
        {
            string name = permissive ? Policy + "_allow" : Policy;
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = {Text(sql.Names.Table(pair.Key))}::regclass AND p.polname = {Text(name)}
                        AND p.polcmd = 'r' AND p.polpermissive = {Boolean(permissive)}
                        AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                        AND p.polqual IS NOT NULL AND p.polwithcheck IS NULL
                        AND pg_catalog.obj_description(p.oid,'pg_policy') = {Text(Stamp + Hash(pair.Value) + ":")}
                            || pg_catalog.md5(p.polqual::text))
                THEN RAISE EXCEPTION 'event_creation_publisher_policy_drift' USING ERRCODE = '55000'; END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = {Text(sql.Names.Table(pair.Key))}::regclass
                        AND p.polname NOT IN ('tenant_isolation','{Policy}','{Policy}_allow')
                        AND EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                            WHERE role_id = 0 OR pg_catalog.pg_has_role('{OwnerRole}',role_id,'USAGE')))
                THEN RAISE EXCEPTION 'event_creation_publisher_unexpected_policy' USING ERRCODE = '55000'; END IF;
                """;
        })));

    private static string SourceGuard(ExploreDbContext context, SourceSql sql, PostgresNativeQueueEnrollment enrollment)
    {
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Names.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
            THEN RAISE EXCEPTION 'event_creation_publisher_namespace_drift' USING ERRCODE = '55000'; END IF;
            """ + string.Join("\n", sql.Sources.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Key)!;
            var declared = context.GetService<IDesignTimeModel>().Model.FindEntityType(source.Key)!;
            var tenant = tenantTables.SingleOrDefault(table => table.TableName == entity.GetTableName());
            string table = Text(sql.Names.Table(source.Key));
            string columns = string.Join(" OR ", entity.GetProperties().Select(property =>
            {
                var declaredProperty = declared.FindProperty(property.Name)!;
                string collation = declaredProperty.GetCollation()
                    ?? (property.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value is true
                        || property.FindAnnotation(LocationUnicodePropertyExtensions.AnnotationName)?.Value is true ? "C" : "default");
                string identity = NpgsqlPropertyExtensions.GetValueGenerationStrategy(declaredProperty) switch
                {
                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn => "d",
                    NpgsqlValueGenerationStrategy.IdentityAlwaysColumn => "a",
                    _ => ""
                };
                return $"""
                    NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = {table}::regclass
                        AND a.attname = {Text(sql.Names.ColumnName(source.Key, property.Name))} AND a.attnum > 0 AND NOT a.attisdropped
                        AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                        AND a.attnotnull = {Boolean(!property.IsNullable)} AND a.attgenerated = '' AND a.attidentity = {Text(identity)}
                        AND (a.attcollation = 0 OR a.attcollation = pg_catalog.to_regcollation({Text("pg_catalog." + sql.Names.Quote(collation))})))
                    """;
            }));
            string primary = string.Join(",", entity.FindPrimaryKey()!.Properties.Select(property =>
                Text(sql.Names.ColumnName(source.Key, property.Name))));
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relkind = 'r' AND t.relpersistence = 'p' AND NOT t.relispartition
                    AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid
                    AND t.relrowsecurity = {Boolean(tenant is not null)} AND t.relforcerowsecurity = {Boolean(tenant is not null)})
                    OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = {table}::regclass
                        AND a.attnum > 0 AND NOT a.attisdropped) <> {entity.GetProperties().Count()}
                    OR {columns}
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = {table}::regclass)
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid = {table}::regclass OR i.inhparent = {table}::regclass)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indrelid = {table}::regclass
                        AND i.indisprimary AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate
                        AND i.indexprs IS NULL AND i.indpred IS NULL
                        AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id ORDER BY k.ord)
                            = ARRAY[{primary}]::name[])
                THEN RAISE EXCEPTION 'event_creation_publisher_source_catalog_drift' USING ERRCODE = '55000'; END IF;
                {(tenant is null ? "" : $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {table}::regclass
                        AND p.polname = 'tenant_isolation' AND p.polcmd = '*' AND p.polpermissive
                        AND p.polroles = ARRAY[0::oid] AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                        AND pg_catalog.obj_description(p.oid,'pg_policy') =
                            {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, sql.Names.Schema))}
                            || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                    THEN RAISE EXCEPTION 'event_creation_publisher_tenant_policy_drift' USING ERRCODE = '55000'; END IF;
                    """)}
                """;
        })) + RelationalKeysGuard(context, sql);
    }

    private static string RelationalKeysGuard(ExploreDbContext context, SourceSql sql)
    {
        string Fields(Type type, IEnumerable<IProperty> properties) =>
            "ARRAY[" + string.Join(",", properties.Select(property => Text(sql.Names.ColumnName(type, property.Name)))) + "]::name[]";
        return string.Join("\n", sql.Sources.Keys.SelectMany(type =>
        {
            var entity = context.Model.FindEntityType(type)!;
            string table = Text(sql.Names.Table(type));
            var keys = entity.GetKeys().Select(key => key.Properties)
                .Concat(entity.GetIndexes().Where(index => index.IsUnique && index.GetFilter() is null)
                    .Select(index => index.Properties));
            return keys.Select(properties => $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class idx ON idx.oid = i.indexrelid
                    JOIN pg_catalog.pg_am method ON method.oid = idx.relam
                    WHERE i.indrelid = {table}::regclass AND i.indisunique AND i.indisvalid AND i.indisready
                        AND i.indimmediate AND NOT i.indisexclusion AND i.indexprs IS NULL AND i.indpred IS NULL
                        AND method.amname = 'btree' AND i.indnkeyatts = {properties.Count} AND i.indnatts = {properties.Count}
                        AND NOT EXISTS (SELECT 1 FROM unnest(i.indclass::oid[]) operator_id
                            JOIN pg_catalog.pg_opclass op ON op.oid = operator_id
                            WHERE NOT op.opcdefault OR op.opcmethod <> idx.relam)
                        AND NOT EXISTS (SELECT 1 FROM unnest(i.indcollation::oid[]) WITH ORDINALITY col(value,ord)
                            JOIN unnest(i.indkey::smallint[]) WITH ORDINALITY k(attnum,ord) ON k.ord = col.ord
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum
                            WHERE col.value <> a.attcollation)
                        AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id ORDER BY k.ord)
                            = {Fields(type, properties)})
                THEN RAISE EXCEPTION 'event_creation_publisher_unique_drift' USING ERRCODE = '55000'; END IF;
                """).Concat(entity.GetForeignKeys().Where(key => sql.Sources.ContainsKey(key.PrincipalEntityType.ClrType))
                .Select(key => $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint fk
                        WHERE fk.contype = 'f' AND fk.convalidated AND NOT fk.condeferrable AND NOT fk.condeferred
                            AND fk.confmatchtype = 's' AND fk.confupdtype = 'a'
                            AND fk.confdeltype = '{(key.DeleteBehavior == DeleteBehavior.Cascade ? "c"
                                : key.DeleteBehavior == DeleteBehavior.SetNull ? "n"
                                : key.DeleteBehavior == DeleteBehavior.Restrict ? "r" : "a")}'
                            AND fk.conrelid = {table}::regclass
                            AND fk.confrelid = {Text(sql.Names.Table(key.PrincipalEntityType.ClrType))}::regclass
                            AND ARRAY(SELECT a.attname FROM unnest(fk.conkey) WITH ORDINALITY k(id,ord)
                                JOIN pg_catalog.pg_attribute a ON a.attrelid = fk.conrelid AND a.attnum = k.id ORDER BY k.ord)
                                = {Fields(type, key.Properties)}
                            AND ARRAY(SELECT a.attname FROM unnest(fk.confkey) WITH ORDINALITY k(id,ord)
                                JOIN pg_catalog.pg_attribute a ON a.attrelid = fk.confrelid AND a.attnum = k.id ORDER BY k.ord)
                                = {Fields(key.PrincipalEntityType.ClrType, key.PrincipalKey.Properties)}
                            AND EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgconstraint = fk.oid)
                            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                                WHERE t.tgconstraint = fk.oid AND t.tgenabled NOT IN ('O','A')))
                    THEN RAISE EXCEPTION 'event_creation_publisher_foreign_key_drift' USING ERRCODE = '55000'; END IF;
                    """));
        }));
    }
}
