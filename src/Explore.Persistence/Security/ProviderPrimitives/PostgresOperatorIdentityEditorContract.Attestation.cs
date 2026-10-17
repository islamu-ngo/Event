using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresOperatorIdentityEditorContract
{
    private static readonly (string Operation, string Signature, string Result)[] Routines =
        [("read", "", "jsonb"), ("pending", "boolean", "jsonb"), ("audit", "uuid", "jsonb"),
            ("write", WriteSignature, "void")];

    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.GlobalLogin, enrollment.RuntimeLogin,
                enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }
            .Any(login => Roles.Contains(login, StringComparer.Ordinal)))
            throw new InvalidOperationException("operator_editor_enrollment_role_collision");
        var sql = new HumanPrincipalSourceSql(context);
        return new(sql.Schema, Roles,
            Routines.Select(routine => new PostgresPrivilegeGuard.Routine(
                "operator_identity_editor_" + routine.Operation, routine.Signature, OwnerRole, [GlobalRole])).ToArray(),
            Sources.Select(source => new PostgresPrivilegeGuard.Relation(
                context.Model.FindEntityType(source.Type)!.GetTableName()!, OwnerRole, [],
                source.Columns.Select(property => new PostgresPrivilegeGuard.Column(sql.ColumnName(source.Type, property),
                    ["SELECT", .. source.Type != typeof(InstanceBootstrapState) ? new[] { "INSERT" } : [],
                        .. source.Type == typeof(SystemSetting) && SettingUpdates.Contains(property, StringComparer.Ordinal)
                            || source.Type == typeof(InstanceBootstrapState) && property == "Id" ? new[] { "UPDATE" } : []]))
                    .ToArray())).ToArray(),
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)]);
    }

    private static string SourceGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string sources = string.Join("\n", Sources.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            if (!entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(source.Columns.Order(StringComparer.Ordinal)))
                throw new InvalidOperationException("operator_editor_source_mapping_changed");
            string table = sql.Table(source.Type);
            string columns = string.Join(" OR ", source.Columns.Select(property =>
            {
                var mapped = entity.FindProperty(property)!;
                string collation = source.Type == typeof(InstanceBootstrapState)
                    && property.EndsWith("Fingerprint", StringComparison.Ordinal) ? "C" : "default";
                return $"""
                    NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                        WHERE a.attrelid = pg_catalog.to_regclass({Text(table)})
                            AND a.attname = {Text(sql.ColumnName(source.Type, property))}
                            AND a.attnum > 0 AND NOT a.attisdropped
                            AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(mapped.GetRelationalTypeMapping().StoreType)}
                            AND a.attnotnull = {(mapped.IsNullable ? "false" : "true")}
                            AND a.attgenerated = '' AND a.attidentity = ''
                            AND (a.attcollation = 0 OR a.attcollation =
                                pg_catalog.to_regcollation({Text("pg_catalog." + sql.Quote(collation))})))
                    """;
            }));
            string keys = string.Join("\n", entity.GetKeys().Select(key => key.Properties)
                .Concat(entity.GetIndexes().Where(index => index.IsUnique && index.GetFilter() is null)
                    .Select(index => index.Properties)).Select(properties => $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i
                        WHERE i.indrelid = pg_catalog.to_regclass({Text(table)})
                            AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate
                            AND i.indpred IS NULL AND i.indexprs IS NULL
                            AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                                JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id
                                WHERE k.ord <= i.indnkeyatts ORDER BY k.ord)
                                = ARRAY[{string.Join(",", properties.Select(property => Text(sql.ColumnName(source.Type, property.Name))))}]::name[])
                    THEN RAISE EXCEPTION 'operator_editor_source_key_drift' USING ERRCODE = '55000'; END IF;
                    """));
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class r WHERE r.oid = pg_catalog.to_regclass({Text(table)})
                    AND r.relkind = 'r' AND r.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid
                    AND NOT r.relrowsecurity AND NOT r.relforcerowsecurity)
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite w WHERE w.ev_class = pg_catalog.to_regclass({Text(table)}))
                    OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({Text(table)})
                        AND a.attnum > 0 AND NOT a.attisdropped) <> {source.Columns.Length}
                    OR {columns}
                THEN RAISE EXCEPTION 'operator_editor_source_drift' USING ERRCODE = '55000'; END IF;
                {keys}
                """;
        }));
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
            THEN RAISE EXCEPTION 'operator_editor_schema_drift' USING ERRCODE = '55000'; END IF;
            {sources}
            """;
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string roles = string.Join(",", Roles.Select(Text));
        string ids = string.Join(",", Routines.Select(routine =>
            $"pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Operation) + "(" + routine.Signature + ")")})"));
        string routines = string.Join("\n", Routines.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Operation) + "(" + routine.Signature + ")")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof
                    AND NOT f.proisstrict AND NOT f.proretset AND f.proargdefaults IS NULL
                    AND f.prorettype = 'pg_catalog.{routine.Result}'::regtype
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.obj_description(f.oid,'pg_proc') =
                        '{Stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex'))
            THEN RAISE EXCEPTION 'operator_editor_routine_drift' USING ERRCODE = '55000'; END IF;
            """));
        string directWrites = string.Join(" OR ", Sources.Select(source => $"""
            pg_catalog.has_table_privilege({Text(enrollment.GlobalLogin)},
                pg_catalog.to_regclass({Text(sql.Table(source.Type))}),'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
            OR pg_catalog.has_any_column_privilege({Text(enrollment.GlobalLogin)},
                pg_catalog.to_regclass({Text(sql.Table(source.Type))}),'INSERT,UPDATE,REFERENCES')
            """));
        return $"""
            {SourceGuard(context, enrollment)}
            {PostgresPrivilegeGuard.BuildAssertSql(Privileges(context, enrollment))}
            {routines}
            IF pg_catalog.has_schema_privilege({Text(enrollment.GlobalLogin)},{Text(sql.Schema)},'CREATE')
                OR {directWrites}
            THEN RAISE EXCEPTION 'operator_editor_ambient_write_authority' USING ERRCODE = '55000'; END IF;
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
                        AND (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND t.relkind = 'S' AND pg_catalog.has_sequence_privilege(r.oid,t.oid,'USAGE,SELECT,UPDATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_database d CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND pg_catalog.has_database_privilege(r.oid,d.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_shdepend d WHERE d.refclassid = 'pg_catalog.pg_authid'::regclass
                    AND d.refobjid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                    AND d.deptype = 'o' AND (d.classid <> 'pg_catalog.pg_proc'::regclass
                        OR d.dbid <> (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database())
                        OR d.objid NOT IN ({ids})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid NOT IN ({ids})
                    AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a
                            WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
            THEN RAISE EXCEPTION 'operator_editor_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        string.Join("\n", Functions(context, enrollment).Select(function => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, function.Operation) + "(" + function.Signature + ")")})
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(function.Body))}
                    AND pg_catalog.obj_description(f.oid,'pg_proc') = {Text(Stamp + Hash(function.Body))})
            THEN RAISE EXCEPTION 'operator_editor_body_drift' USING ERRCODE = '55000'; END IF;
            """));
}
