using Explore.Domain;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Security;

public static partial class PostgresIdempotencyReceiptContract
{
    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.GlobalLogin, enrollment.RuntimeLogin,
            enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(Roles.Contains))
            throw new InvalidOperationException("idempotency_receipt_enrollment_collision");
        var sql = new IdempotencyReceiptSql(context);
        string table = context.Model.FindEntityType(typeof(IdempotencyRecord))!.GetTableName()!;
        return new(sql.Schema, Roles,
            Routines.Select(routine => new PostgresPrivilegeGuard.Routine(Prefix + routine.Name, routine.Signature,
                routine.Discovery ? DiscoveryOwnerRole : OwnerRole, [routine.Discovery ? GlobalRole : RuntimeRole])).ToArray(),
            [
                new(table, OwnerRole, [], ReceiptFields.Select(property =>
                    new PostgresPrivilegeGuard.Column(sql.ColumnName(typeof(IdempotencyRecord), property),
                        property is "StatusCode" or "ResponseBody" or "ContentType"
                            ? ["SELECT", "INSERT", "UPDATE"] : ["SELECT", "INSERT"])).ToArray()),
                new(table, DiscoveryOwnerRole, [], new[] { "TenantId", "ExpiresAt", "StatusCode", "Id" }
                    .Select(property => new PostgresPrivilegeGuard.Column(sql.ColumnName(typeof(IdempotencyRecord), property), ["SELECT"])).ToArray()),
                new(OwnershipTable, OwnerRole, ["SELECT", "INSERT", "DELETE"], [])
            ],
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(DiscoveryOwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)
            ]);
    }

    private static string SourceGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool installed)
    {
        var sql = new IdempotencyReceiptSql(context);
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(IdempotencyRecord))!;
        if (!entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(ReceiptFields.Order(StringComparer.Ordinal))
            || PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
                .Any(table => table.TableName == entity.GetTableName()))
            throw new InvalidOperationException("idempotency_receipt_source_mapping_changed");
        string table = Text(sql.Table(typeof(IdempotencyRecord)));
        string columns = string.Join(" OR ", entity.GetProperties().Select(property =>
        {
            string? defaultSql = property.GetDefaultValueSql();
            string collation = property.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value is true ? "C" : "default";
            return $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a LEFT JOIN pg_catalog.pg_attrdef d
                    ON d.adrelid = a.attrelid AND d.adnum = a.attnum
                    WHERE a.attrelid = pg_catalog.to_regclass({table})
                        AND a.attname = {Text(sql.ColumnName(typeof(IdempotencyRecord), property.Name))}
                        AND a.attnum > 0 AND NOT a.attisdropped
                        AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                        AND a.attnotnull = {(!property.IsNullable).ToString().ToLowerInvariant()}
                        AND a.attgenerated = '' AND a.attidentity = ''
                        AND {(defaultSql is null ? "NOT a.atthasdef AND d.oid IS NULL"
                            : $"a.atthasdef AND pg_catalog.pg_get_expr(d.adbin,d.adrelid) = {Text(defaultSql)}")}
                        AND (a.attcollation = 0 OR a.attcollation = pg_catalog.to_regcollation({Text("pg_catalog." + sql.Quote(collation))}))
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_collation col
                            WHERE col.oid = a.attcollation AND NOT col.collisdeterministic))
                """;
        }));
        string indexes = string.Join("\n", entity.GetKeys().Select(key => IndexGuard(context, table,
            key.Properties.Select(property => property.Name).ToArray(), [], true, key.IsPrimaryKey(), null, null, null))
            .Concat(entity.GetIndexes().Select(index => IndexGuard(context, table,
                index.Properties.Select(property => property.Name).ToArray(), [], index.IsUnique, false,
                index.GetDatabaseName(), index.GetFilter() is { } filter ? "(" + filter + ")" : null, index.IsDescending))));
        if (installed)
            indexes += "\n" + NativeIndexGuard(context);
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relkind = 'r' AND t.relpersistence = 'p' AND NOT t.relispartition
                    AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid
                    AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = pg_catalog.to_regclass({table}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({table}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i
                    WHERE i.inhrelid = pg_catalog.to_regclass({table}) OR i.inhparent = pg_catalog.to_regclass({table}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                    WHERE t.tgrelid = pg_catalog.to_regclass({table}) AND NOT t.tgisinternal)
                OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                    AND a.attnum > 0 AND NOT a.attisdropped) <> {ReceiptFields.Length}
                OR {columns}
                OR (SELECT count(*) FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({table})
                    AND c.contype <> 'n') <> {entity.GetKeys().Count()}
                OR (SELECT count(*) FROM pg_catalog.pg_index i WHERE i.indrelid = pg_catalog.to_regclass({table}))
                    <> {entity.GetKeys().Count() + entity.GetIndexes().Count() + (installed ? 2 : 0)}
            THEN RAISE EXCEPTION 'idempotency_receipt_source_catalog_drift' USING ERRCODE = '55000'; END IF;
            {indexes}
            """;
    }

    private static string IndexGuard(ExploreDbContext context, string table, string[] keys, string[] includes,
        bool unique, bool primary, string? name, string? predicate, IReadOnlyList<bool>? descending)
    {
        var sql = new IdempotencyReceiptSql(context);
        string Column(string property) => Text(sql.ColumnName(typeof(IdempotencyRecord), property));
        string flags = string.Join(",", keys.Select((_, index) =>
            descending is not null && (descending.Count == 0 || descending[index]) ? "3" : "0"));
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class idx ON idx.oid = i.indexrelid
                JOIN pg_catalog.pg_am method ON method.oid = idx.relam
                WHERE i.indrelid = pg_catalog.to_regclass({table})
                    {(name is null ? "" : $"AND idx.relname = {Text(name)}")}
                    AND method.amname = 'btree' AND idx.relpersistence = 'p'
                    AND i.indisunique = {unique.ToString().ToLowerInvariant()} AND i.indisprimary = {primary.ToString().ToLowerInvariant()}
                    AND i.indisvalid AND i.indisready AND i.indimmediate AND NOT i.indisexclusion AND NOT i.indisreplident
                    AND i.indexprs IS NULL AND i.indnkeyatts = {keys.Length} AND i.indnatts = {keys.Length + includes.Length}
                    AND ARRAY(SELECT flag FROM unnest(i.indoption::smallint[]) flag) = ARRAY[{flags}]::smallint[]
                    AND NOT EXISTS (SELECT 1 FROM unnest(i.indclass::oid[]) operator_id
                        JOIN pg_catalog.pg_opclass op ON op.oid = operator_id
                        WHERE NOT op.opcdefault OR op.opcmethod <> idx.relam)
                    AND NOT EXISTS (SELECT 1 FROM unnest(i.indcollation::oid[]) WITH ORDINALITY col(value,ord)
                        JOIN unnest(i.indkey::smallint[]) WITH ORDINALITY k(attnum,ord) ON k.ord = col.ord
                        JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum
                        WHERE col.value <> a.attcollation)
                    AND {(predicate is null ? "i.indpred IS NULL" : $"pg_catalog.pg_get_expr(i.indpred,i.indrelid) = {Text(predicate)}")}
                    AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                        JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id ORDER BY k.ord)
                        = ARRAY[{string.Join(",", keys.Concat(includes).Select(Column))}]::name[]
                    AND {(primary ? $"""
                        EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conindid = i.indexrelid AND c.contype = 'p'
                            AND c.convalidated AND NOT c.condeferrable AND NOT c.condeferred)
                        """ : "true")})
            THEN RAISE EXCEPTION 'idempotency_receipt_index_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string NativeIndexGuard(ExploreDbContext context)
    {
        string table = Text(new IdempotencyReceiptSql(context).Table(typeof(IdempotencyRecord)));
        string predicate = "(" + new IdempotencyReceiptSql(context).Column(typeof(IdempotencyRecord), "StatusCode")
            .Replace("\"", "", StringComparison.Ordinal) + " <> '-1'::integer)";
        return IndexGuard(context, table, ["ExpiresAt", "Id"], ["TenantId"], false, false,
            "idempotency_receipt_expiry", predicate, null) + "\n"
            + IndexGuard(context, table, ["TenantId", "ExpiresAt", "Id"], [], false, false,
                "idempotency_receipt_tenant_expiry", predicate, null);
    }

    private static string InternalGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new IdempotencyReceiptSql(context);
        string table = Text(sql.Name(OwnershipTable));
        (string Name, string Type, bool Nullable)[] columns =
        [
            ("receipt_id", "uuid", false), ("tenant_id", "uuid", false), ("nonce_hash", "bytea", false),
            ("backend", "integer", false), ("original_transaction", "bigint", true)
        ];
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                AND t.relkind = 'r' AND t.relpersistence = 'p' AND NOT t.relispartition
                AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity
                AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = pg_catalog.to_regclass({table}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({table}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i
                    WHERE i.inhrelid = pg_catalog.to_regclass({table}) OR i.inhparent = pg_catalog.to_regclass({table}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = pg_catalog.to_regclass({table}) AND NOT t.tgisinternal)
                OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                    AND a.attnum > 0 AND NOT a.attisdropped) <> {columns.Length}
                OR {string.Join(" OR ", columns.Select(column => $"""
                    NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                        AND a.attname = {Text(column.Name)} AND a.attnum > 0 AND NOT a.attisdropped
                        AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(column.Type)}
                        AND a.attnotnull = {(!column.Nullable).ToString().ToLowerInvariant()}
                        AND NOT a.atthasdef AND a.attgenerated = '' AND a.attidentity = '' AND a.attcollation = 0)
                    """))}
                OR (SELECT count(*) FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({table})
                    AND c.contype <> 'n') <> 2
                OR (SELECT count(*) FROM pg_catalog.pg_index i WHERE i.indrelid = pg_catalog.to_regclass({table})) <> 1
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c JOIN pg_catalog.pg_index i ON i.indexrelid = c.conindid
                    JOIN pg_catalog.pg_class idx ON idx.oid = i.indexrelid
                    JOIN pg_catalog.pg_am am ON am.oid = idx.relam
                    WHERE c.conrelid = pg_catalog.to_regclass({table}) AND c.contype = 'p'
                        AND c.convalidated AND NOT c.condeferrable AND NOT c.condeferred
                        AND i.indisunique AND i.indisprimary AND i.indisvalid AND i.indisready AND i.indimmediate
                        AND i.indnkeyatts = 1 AND i.indnatts = 1 AND i.indexprs IS NULL AND i.indpred IS NULL
                        AND am.amname = 'btree'
                        AND NOT EXISTS (SELECT 1 FROM unnest(i.indoption::smallint[]) flag WHERE flag <> 0)
                        AND NOT EXISTS (SELECT 1 FROM unnest(i.indclass::oid[]) opid
                            JOIN pg_catalog.pg_opclass op ON op.oid = opid WHERE NOT op.opcdefault OR op.opcmethod <> idx.relam)
                        AND ARRAY(SELECT a.attname FROM unnest(c.conkey) WITH ORDINALITY k(id,ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.id ORDER BY k.ord)
                            = ARRAY['receipt_id']::name[])
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({table})
                    AND c.contype = 'c' AND c.convalidated AND NOT c.connoinherit AND NOT c.condeferrable
                    AND pg_catalog.pg_get_expr(c.conbin,c.conrelid) = '(octet_length(nonce_hash) = 32)')
            THEN RAISE EXCEPTION 'idempotency_receipt_ownership_catalog_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new IdempotencyReceiptSql(context);
        string ids = string.Join(",", Routines.Select(routine =>
            $"pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Name) + "(" + routine.Signature + ")")})"));
        string roles = string.Join(",", Roles.Select(Text));
        string routines = string.Join("\n", Routines.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Name) + "(" + routine.Signature + ")")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                    AND NOT f.proretset AND f.proargdefaults IS NULL
                    AND f.prorettype = {Text("pg_catalog." + (routine.Result switch { "boolean" => "bool", "integer" => "int4", _ => routine.Result }))}::regtype
                    AND f.provariadic = 0 AND f.proallargtypes IS NULL AND f.proargmodes IS NULL
                    AND f.proargnames = ARRAY[{string.Join(",", routine.Arguments.Split(',').Select(argument => Text(argument.Split(' ')[0])))}]::text[]
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.obj_description(f.oid,'pg_proc') =
                        '{Stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex'))
            THEN RAISE EXCEPTION 'idempotency_receipt_routine_drift' USING ERRCODE = '55000'; END IF;
            """));
        string services = string.Join("\n", new[] { enrollment.RuntimeLogin, enrollment.GlobalLogin }
            .SelectMany(login => new[] { sql.Table(typeof(IdempotencyRecord)), sql.Name(OwnershipTable) }.Select(table => $"""
                IF (pg_catalog.has_table_privilege({Text(login)},pg_catalog.to_regclass({Text(table)}),
                        'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    OR {PostgresPrivilegeGuard.MaintainPrivilegeSql(Text(login), $"pg_catalog.to_regclass({Text(table)})", false)})
                    OR pg_catalog.has_any_column_privilege({Text(login)},pg_catalog.to_regclass({Text(table)}),'SELECT,INSERT,UPDATE,REFERENCES')
                THEN RAISE EXCEPTION 'idempotency_receipt_ambient_service_access' USING ERRCODE = '55000'; END IF;
                """)));
        return $"""
            {SourceGuard(context, enrollment, true)}
            {InternalGuard(context, enrollment)}
            {PostgresPrivilegeGuard.BuildAssertSql(Privileges(context, enrollment))}
            {routines}
            {services}
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                    AND pg_catalog.shobj_description(r.oid,'pg_authid') IS DISTINCT FROM '{Stamp}role:' || r.rolname)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                    AND NOT pg_catalog.has_schema_privilege(r.oid,{Text(sql.Schema)},'USAGE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
                    CROSS JOIN pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                        AND n.nspname <> {Text(sql.Schema)} AND n.nspname NOT IN ('pg_catalog','information_schema')
                        AND n.nspname NOT LIKE 'pg_toast%' AND t.relkind IN ('r','p','v','m','f')
                        AND ((pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", false)})
                            OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND t.relkind = 'S' AND pg_catalog.has_sequence_privilege(r.oid,t.oid,'USAGE,SELECT,UPDATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_database d CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND pg_catalog.has_database_privilege(r.oid,d.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_shdepend d WHERE d.refclassid = 'pg_catalog.pg_authid'::regclass
                    AND d.refobjid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})) AND d.deptype = 'o'
                    AND (d.classid <> 'pg_catalog.pg_proc'::regclass
                        OR d.dbid <> (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database()) OR d.objid NOT IN ({ids})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid NOT IN ({ids})
                    AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a
                            WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgfoid IN ({ids}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Schema)}
                    AND (pg_catalog.has_schema_privilege({Text(enrollment.RuntimeLogin)},n.oid,'CREATE')
                        OR pg_catalog.has_schema_privilege({Text(enrollment.GlobalLogin)},n.oid,'CREATE')))
            THEN RAISE EXCEPTION 'idempotency_receipt_closed_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        string.Join("\n", Routines.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Name) + "(" + routine.Signature + ")")})
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex')
                        = {Text(Hash(Body(context, enrollment, routine.Name)))})
            THEN RAISE EXCEPTION 'idempotency_receipt_body_drift' USING ERRCODE = '55000'; END IF;
            """));
}
