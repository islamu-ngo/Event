using Explore.Domain;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresOrdinarySettingsContract
{
    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.GlobalLogin, enrollment.RuntimeLogin,
            enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(login => Roles.Contains(login)))
            throw new InvalidOperationException("ordinary_settings_enrollment_collision");
        var sql = new HumanPrincipalSourceSql(context);
        return new(sql.Schema, Roles,
            Routines.Select(routine => new PostgresPrivilegeGuard.Routine(Prefix + routine.Name, routine.Signature,
                OwnerRole, routine.Result == "trigger" ? [OwnerRole] : [RuntimeRole, GlobalRole])).ToArray(),
            [
                .. Sources.Select(type => new PostgresPrivilegeGuard.Relation(context.Model.FindEntityType(type)!.GetTableName()!,
                    OwnerRole, type == typeof(SystemSetting) ? ["UPDATE"] : ["UPDATE", "DELETE"],
                    Fields(type).Select(field => new PostgresPrivilegeGuard.Column(sql.ColumnName(type, field.Property),
                        ["SELECT", "INSERT"])).ToArray())),
                new(SourceTable, OwnerRole, [], [new("id", ["SELECT"]), new("revision", ["SELECT", "UPDATE"]),
                    new("tenant_revision", ["SELECT", "UPDATE"])]),
                new(AttemptTable, OwnerRole, ["SELECT", "INSERT", "UPDATE"], [])
            ],
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)
            ]);
    }

    private static string SourceGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string tables = string.Join("\n", Sources.Select(type =>
        {
            var entity = context.Model.FindEntityType(type)!;
            if (!entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(Fields(type).Select(field => field.Property).Order(StringComparer.Ordinal)))
                throw new InvalidOperationException("ordinary_settings_mapping_changed");
            string table = Text(sql.Table(type));
            string columns = string.Join(" OR ", entity.GetProperties().Select(property =>
            {
                string collation = property.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value is true ? "C" : "default";
                return $"""
                    NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                        AND a.attname = {Text(sql.ColumnName(type, property.Name))} AND a.attnum > 0 AND NOT a.attisdropped
                        AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                        AND a.attnotnull = {(!property.IsNullable).ToString().ToLowerInvariant()} AND a.attgenerated = '' AND a.attidentity = ''
                        AND (a.attcollation = 0 OR a.attcollation = pg_catalog.to_regcollation({Text("pg_catalog." + sql.Quote(collation))})))
                    """;
            }));
            string keys = string.Join("\n", entity.GetKeys().Select(key => (key.Properties, Filter: (string?)null, Unique: true))
                .Concat(entity.GetIndexes().Select(index => (index.Properties, Filter: index.GetFilter(), Unique: index.IsUnique)))
                .Select(index => $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indrelid = pg_catalog.to_regclass({table})
                        AND i.indisunique = {index.Unique.ToString().ToLowerInvariant()}
                        AND i.indisvalid AND i.indisready AND i.indimmediate AND i.indexprs IS NULL
                        AND i.indnkeyatts = {index.Properties.Count} AND i.indnatts = {index.Properties.Count}
                        AND NOT EXISTS (SELECT 1 FROM unnest(i.indoption::smallint[]) o(flag) WHERE flag <> 0)
                        AND NOT EXISTS (SELECT 1 FROM unnest(i.indclass::oid[]) operator_id
                            JOIN pg_catalog.pg_opclass o ON o.oid = operator_id JOIN pg_catalog.pg_am am ON am.oid = o.opcmethod
                            WHERE NOT o.opcdefault OR am.amname <> 'btree')
                        AND NOT EXISTS (SELECT 1 FROM unnest(i.indcollation::oid[]) WITH ORDINALITY col(value,ord)
                            JOIN unnest(i.indkey::smallint[]) WITH ORDINALITY k(attnum,ord) ON k.ord = col.ord
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum
                            WHERE col.value <> a.attcollation)
                        AND {(index.Filter is null ? "i.indpred IS NULL" : $"pg_catalog.pg_get_expr(i.indpred,i.indrelid) = {Text("(" + index.Filter + ")")}")}
                        AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id
                            WHERE k.ord <= i.indnkeyatts ORDER BY k.ord)
                            = ARRAY[{string.Join(",", index.Properties.Select(property => Text(sql.ColumnName(type, property.Name))))}]::name[])
                    THEN RAISE EXCEPTION 'ordinary_settings_source_key_drift' USING ERRCODE = '55000'; END IF;
                    """));
            bool tenant = type != typeof(SystemSetting);
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relkind = 'r' AND t.relpersistence = 'p'
                    AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid
                    AND ({(type == typeof(SystemSetting)
                        ? PostgresLocalIdentitySystemSettingPolicies.Compatible(OwnerRole, "t")
                        : $"t.relrowsecurity = {tenant.ToString().ToLowerInvariant()} AND t.relforcerowsecurity = {tenant.ToString().ToLowerInvariant()}")}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({table}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i
                        WHERE i.inhrelid = pg_catalog.to_regclass({table}) OR i.inhparent = pg_catalog.to_regclass({table}))
                    OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                        AND a.attnum > 0 AND NOT a.attisdropped) <> {Fields(type).Length} OR {columns}
                    OR (SELECT count(*) FROM pg_catalog.pg_index i WHERE i.indrelid = pg_catalog.to_regclass({table}))
                        <> {entity.GetKeys().Count() + entity.GetIndexes().Count()}
                THEN RAISE EXCEPTION 'ordinary_settings_source_catalog_drift' USING ERRCODE = '55000'; END IF;
                {keys}
                """;
        }));
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
            THEN RAISE EXCEPTION 'ordinary_settings_namespace_owner' USING ERRCODE = '55000'; END IF;
            {tables}
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                WHERE t.oid = {Text(sql.Table(typeof(Tenant)))}::regclass AND t.relkind = 'r' AND t.relpersistence = 'p'
                    AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    WHERE a.attrelid = {Text(sql.Table(typeof(Tenant)))}::regclass
                        AND a.attname = {Text(sql.ColumnName(typeof(Tenant), nameof(Tenant.Id)))}
                        AND a.attnum > 0 AND NOT a.attisdropped AND a.atttypid = 'uuid'::regtype AND a.attnotnull
                        AND a.attgenerated = '' AND a.attidentity = '')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i
                    WHERE i.inhrelid = {Text(sql.Table(typeof(Tenant)))}::regclass
                        OR i.inhparent = {Text(sql.Table(typeof(Tenant)))}::regclass)
            THEN RAISE EXCEPTION 'ordinary_settings_tenant_source_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static (string Name, string Type, bool Nullable)[] InternalColumns(string table) => table == SourceTable
        ? [("id", "smallint", false), ("revision", "bigint", false), ("tenant_revision", "bigint", false)]
        : [
            ("attempt", "bigint", false), ("backend", "integer", false), ("tenant", "uuid", true), ("user_id", "uuid", true),
            ("request", "jsonb", false), ("image", "jsonb", false), ("completed", "jsonb", false), ("pending", "jsonb", true),
            ("observed", "boolean", false), ("finished", "boolean", false), ("disclosure_tenants", "uuid[]", false)
        ];

    private static string InternalGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return string.Join("\n", new[] { SourceTable, AttemptTable }.Select(name =>
        {
            string table = Text(sql.Name(name));
            var columns = InternalColumns(name);
            string shape = string.Join(" OR ", columns.Select(column => $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                    AND a.attname = {Text(column.Name)} AND a.attnum > 0 AND NOT a.attisdropped
                    AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(column.Type)}
                    AND a.attnotnull = {(!column.Nullable).ToString().ToLowerInvariant()}
                    AND a.attgenerated = '' AND a.attidentity = '' AND NOT a.atthasdef AND a.attcollation = 0)
                """));
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relkind = 'r' AND t.relpersistence = 'p' AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity
                    AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                    OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                        AND a.attnum > 0 AND NOT a.attisdropped) <> {columns.Length}
                    OR {shape}
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({table}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid = pg_catalog.to_regclass({table})
                        OR i.inhparent = pg_catalog.to_regclass({table}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = pg_catalog.to_regclass({table})
                        AND NOT t.tgisinternal {(name == AttemptTable ? $"AND t.tgname <> '{Prefix}settlement'" : "")})
                    OR (SELECT count(*) FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({table})
                        AND c.contype <> 'n') <> 2
                    OR (SELECT count(*) FROM pg_catalog.pg_index i WHERE i.indrelid = pg_catalog.to_regclass({table})) <> 1
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c JOIN pg_catalog.pg_index i ON i.indexrelid = c.conindid
                        WHERE c.conrelid = pg_catalog.to_regclass({table}) AND c.contype = 'p' AND c.convalidated
                            AND NOT c.condeferrable AND NOT c.condeferred AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate
                            AND i.indpred IS NULL AND i.indexprs IS NULL
                            AND ARRAY(SELECT a.attname FROM unnest(c.conkey) WITH ORDINALITY k(id,ord)
                                JOIN pg_catalog.pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.id ORDER BY k.ord)
                                = ARRAY[{(name == SourceTable ? "'id'" : "'attempt','backend'")}]::name[])
                    {(name == SourceTable ? $"""
                        OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({table})
                            AND c.contype = 'c' AND c.convalidated AND NOT c.connoinherit
                            AND pg_catalog.pg_get_expr(c.conbin,c.conrelid) = '(id = 1)')
                        """ : "")}
                THEN RAISE EXCEPTION 'ordinary_settings_private_catalog_drift' USING ERRCODE = '55000'; END IF;
                """;
        }));
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string ids = string.Join(",", Routines.Select(routine =>
            $"pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Name) + "(" + routine.Signature + ")")})"));
        string roles = string.Join(",", Roles.Select(Text));
        string nested = string.Join(",", PostgresDiscoveryDisclosureContract.NestedRoutines.Select(routine =>
            $"pg_catalog.to_regprocedure({Text(PostgresDiscoveryDisclosureContract.FunctionSql(context, routine.Name) + "(" + routine.Signature + ")")})"));
        string routines = string.Join("\n", Routines.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Name) + "(" + routine.Signature + ")")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                    AND NOT f.proretset AND f.proargdefaults IS NULL AND f.prorettype = 'pg_catalog.{routine.Result}'::regtype
                    AND f.provariadic = 0 AND f.proallargtypes IS NULL AND f.proargmodes IS NULL
                    AND f.proargnames IS NOT DISTINCT FROM {(routine.Arguments.Length == 0 ? "NULL::text[]"
                        : "ARRAY[" + string.Join(",", routine.Arguments.Split(',').Select(argument => Text(argument.Split(' ')[0]))) + "]::text[]")}
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.obj_description(f.oid,'pg_proc') =
                        '{Stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex'))
            THEN RAISE EXCEPTION 'ordinary_settings_routine_drift' USING ERRCODE = '55000'; END IF;
            """));
        string triggers = string.Join("\n", Sources.SelectMany(type => new[] { ("changed", 62), ("row", 29) }
            .Select(trigger => TriggerGuard(context, sql.Table(type), trigger.Item1, trigger.Item2, false))))
            + "\n" + TriggerGuard(context, sql.Name(AttemptTable), "settlement", 21, true)
            + "\n" + TriggerGuard(context, sql.Table(typeof(Tenant)), "tenant_changed", 29, false)
            + "\n" + TriggerGuard(context, sql.Table(typeof(Tenant)), "tenant_truncated", 32, false);
        string policies = string.Join("\n", Sources.Where(type => type != typeof(SystemSetting)).Select(type => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(sql.Table(type))}::regclass
                AND p.polname = '{Policy}' AND p.polcmd = '*' AND p.polpermissive
                AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true'
                AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid) = 'true')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(sql.Table(type))}::regclass
                    AND p.polname NOT IN ('{Policy}','{PostgresTenantRowLevelSecurityModel.PolicyName}')
                    AND (0::oid = ANY(p.polroles) OR EXISTS (SELECT 1 FROM unnest(p.polroles) r
                        WHERE pg_catalog.pg_has_role('{OwnerRole}',r,'USAGE'))))
            THEN RAISE EXCEPTION 'ordinary_settings_complete_source_policy_drift' USING ERRCODE = '55000'; END IF;
            """));
        string tenantPolicies = string.Join("\n", Sources.Where(type => type != typeof(SystemSetting)).Select(type =>
        {
            var metadata = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
                .Single(table => table.TableName == context.Model.FindEntityType(type)!.GetTableName());
            string fingerprint = PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(metadata, sql.Schema);
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(sql.Table(type))}::regclass
                    AND p.polname = '{PostgresTenantRowLevelSecurityModel.PolicyName}' AND p.polcmd = '*'
                    AND p.polroles = ARRAY[0::oid] AND p.polpermissive AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                    AND pg_catalog.obj_description(p.oid,'pg_policy') =
                        {Text(fingerprint)} || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                THEN RAISE EXCEPTION 'ordinary_settings_tenant_policy_drift' USING ERRCODE = '55000'; END IF;
                """;
        }));
        string ambient = string.Join("\n", Sources.Select(type => $"""
            IF pg_catalog.has_table_privilege({Text(enrollment.GlobalLogin)},{Text(sql.Table(type))}::regclass,
                    'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                OR pg_catalog.has_any_column_privilege({Text(enrollment.GlobalLogin)},{Text(sql.Table(type))}::regclass,'INSERT,UPDATE,REFERENCES')
            THEN RAISE EXCEPTION 'ordinary_settings_global_ambient_write' USING ERRCODE = '55000'; END IF;
            """));
        string privateAccess = string.Join("\n", new[] { enrollment.RuntimeLogin, enrollment.GlobalLogin }
            .SelectMany(login => new[] { SourceTable, AttemptTable }.Select(table => $"""
                IF pg_catalog.has_table_privilege({Text(login)},{Text(sql.Name(table))}::regclass,
                        'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    OR pg_catalog.has_any_column_privilege({Text(login)},{Text(sql.Name(table))}::regclass,'SELECT,INSERT,UPDATE,REFERENCES')
                THEN RAISE EXCEPTION 'ordinary_settings_private_service_access' USING ERRCODE = '55000'; END IF;
                """)));
        return $"""
            {SourceGuard(context, enrollment)}
            {PostgresPrivilegeGuard.BuildAssertSql(Privileges(context, enrollment))}
            {InternalGuard(context, enrollment)}
            {DisclosureGuard(context)}
            {routines}
            {triggers}
            {policies}
            {tenantPolicies}
            {ambient}
            {privateAccess}
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
                    AND d.refobjid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})) AND d.deptype = 'o'
                    AND (d.classid <> 'pg_catalog.pg_proc'::regclass
                        OR d.dbid <> (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database()) OR d.objid NOT IN ({ids})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid NOT IN ({ids})
                    AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a
                            WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                                AND NOT (f.oid IN ({nested}) AND a.grantee = pg_catalog.to_regrole('{OwnerRole}')::oid
                                    AND a.grantor = pg_catalog.to_regrole('{PostgresDiscoveryDisclosureContract.OwnerRole}')::oid
                                    AND a.privilege_type = 'EXECUTE' AND NOT a.is_grantable))))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n
                    WHERE n.nspname = {Text(sql.Schema)}
                        AND (pg_catalog.has_schema_privilege({Text(enrollment.GlobalLogin)},n.oid,'CREATE')
                            OR pg_catalog.has_schema_privilege({Text(enrollment.RuntimeLogin)},n.oid,'CREATE')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgfoid IN ({ids})
                    AND NOT (t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, "settlement") + "()")})
                            AND t.tgrelid = {Text(sql.Name(AttemptTable))}::regclass AND t.tgname = '{Prefix}settlement'
                        OR t.tgrelid = {Text(sql.Table(typeof(Tenant)))}::regclass
                            AND (t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, "tenant_changed") + "()")})
                                    AND t.tgname = '{Prefix}tenant_changed'
                                OR t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, "tenant_truncated") + "()")})
                                    AND t.tgname = '{Prefix}tenant_truncated')
                        OR t.tgrelid IN ({string.Join(",", Sources.Select(type => Text(sql.Table(type)) + "::regclass"))})
                            AND (t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, "changed") + "()")})
                                    AND t.tgname = '{Prefix}changed'
                                OR t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, "row") + "()")})
                                    AND t.tgname = '{Prefix}row')))
            THEN RAISE EXCEPTION 'ordinary_settings_closed_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string TriggerGuard(ExploreDbContext context, string table, string operation, int type, bool deferred) => $"""
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = {Text(table)}::regclass
            AND t.tgname = '{Prefix}{operation}' AND NOT t.tgisinternal AND t.tgtype = {type} AND t.tgenabled = 'A'
            AND t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, operation) + "()")})
            AND t.tgqual IS NULL AND t.tgnargs = 0 AND t.tgargs = ''::bytea AND t.tgattr = ''::int2vector
            AND t.tgconstrrelid = 0 AND t.tgconstrindid = 0
            AND t.tgdeferrable = {deferred.ToString().ToLowerInvariant()} AND t.tginitdeferred = {deferred.ToString().ToLowerInvariant()}
            AND {(deferred ? "t.tgconstraint <> 0" : "t.tgconstraint = 0")}
            AND t.tgoldtable IS NULL AND t.tgnewtable IS NULL)
        THEN RAISE EXCEPTION 'ordinary_settings_trigger_drift' USING ERRCODE = '55000'; END IF;
        """;

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        string.Join("\n", Routines.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Name) + "(" + routine.Signature + ")")})
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') =
                        {Text(Hash(Body(context, enrollment, routine.Name)))})
            THEN RAISE EXCEPTION 'ordinary_settings_body_drift' USING ERRCODE = '55000'; END IF;
            """));
}
