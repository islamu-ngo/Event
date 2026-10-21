using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Security;

public static partial class PostgresSupportAdministrationContract
{
    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        RequireInventory(context);
        enrollment.Validate();
        if (new[] { enrollment.RuntimeLogin, enrollment.GlobalLogin, enrollment.MigratorLogin,
                enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(Roles.Contains))
            throw new InvalidOperationException("support_administration_enrollment_collision");
        var sql = new HumanPrincipalSourceSql(context);
        return new(sql.Schema, Roles,
            Routines.Select(routine => new PostgresPrivilegeGuard.Routine(Prefix + routine.Name,
                routine.Signature, OwnerRole, routine.Result == "trigger" ? [OwnerRole] : [RuntimeRole])).ToArray(),
            [
                .. Inventory.Select(source => new PostgresPrivilegeGuard.Relation(
                    context.Model.FindEntityType(source.Type)!.GetTableName()!, OwnerRole, [],
                    source.Properties.Select(property => new PostgresPrivilegeGuard.Column(sql.ColumnName(source.Type, property),
                        ["SELECT", .. Mutable(source.Type) ? new[] { "INSERT" } : [], .. UpdateColumns(source.Type).Contains(property, StringComparer.Ordinal)
                            ? new[] { "UPDATE" } : []])).ToArray())),
                new(SourceTable, OwnerRole, [], [new("id", ["SELECT"]), new("revision", ["SELECT", "UPDATE"])]),
                new(AttemptTable, OwnerRole, ["SELECT", "INSERT", "UPDATE"], [])
            ],
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false)]);
    }

    private static string OriginalSources(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        PostgresPolicySourceSetContract.OriginalSourceAssertionSql(context, enrollment) + "\n"
        + PostgresHumanPrincipalSourceContract.OriginalSourceAssertionSql(context, enrollment) + "\n"
        + PostgresHumanAuthorizationRouteContract.OriginalSourceAssertionSql(context, enrollment) + "\n"
        + PostgresHumanSupportAccessSourceContract.OriginalSourceAssertionSql(context, enrollment);

    private static string SourceGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        RequireInventory(context);
        var sql = new HumanPrincipalSourceSql(context);
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        return string.Join("\n", Inventory.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            string table = Text(sql.Table(source.Type));
            bool tenant = tenantTables.Any(candidate => candidate.TableName == entity.GetTableName());
            string columns = string.Join(" OR ", source.Properties.Select(name =>
            {
                var property = entity.FindProperty(name)!;
                string collation = property.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value is true
                    ? "C" : "default";
                string? expectedDefault = source.Type == typeof(Explore.Domain.TenantUser) ? name switch
                {
                    "StatusId" => ((int)Explore.Domain.Enums.TenantUserStatusEnum.Active).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "CreatedAt" => "now()",
                    _ => null
                } : null;
                string defaultCheck = expectedDefault is null ? "NOT a.atthasdef" : $"""
                    a.atthasdef AND EXISTS (SELECT 1 FROM pg_catalog.pg_attrdef d
                        WHERE d.adrelid = a.attrelid AND d.adnum = a.attnum
                            AND pg_catalog.pg_get_expr(d.adbin,d.adrelid) = {Text(expectedDefault)})
                    """;
                return $"""
                    NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                        AND a.attname = {Text(sql.ColumnName(source.Type, name))} AND a.attnum > 0 AND NOT a.attisdropped
                        AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                        AND a.attnotnull = {(!property.IsNullable).ToString().ToLowerInvariant()}
                        AND a.attgenerated = '' AND a.attidentity = '' AND {defaultCheck}
                        AND (a.attcollation = 0 OR a.attcollation = pg_catalog.to_regcollation({Text("pg_catalog." + sql.Quote(collation))})))
                    """;
            }));
            string keyColumns(IReadOnlyList<IProperty> properties, Type type) =>
                "ARRAY[" + string.Join(",", properties.Select(property => Text(sql.ColumnName(type, property.Name)))) + "]::name[]";
            string keys = string.Join("\n", entity.GetKeys().Select(key => (key.Properties, Filter: (string?)null))
                .Concat(entity.GetIndexes().Where(index => index.IsUnique).Select(index => (index.Properties, Filter: index.GetFilter())))
                .Select(index => $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indrelid = pg_catalog.to_regclass({table})
                        AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate AND i.indexprs IS NULL
                        AND i.indnkeyatts = {index.Properties.Count} AND i.indnatts = {index.Properties.Count}
                        AND NOT EXISTS (SELECT 1 FROM unnest(i.indoption::smallint[]) o(flag) WHERE flag <> 0)
                        AND NOT EXISTS (SELECT 1 FROM unnest(i.indclass::oid[]) operator_id
                            JOIN pg_catalog.pg_opclass o ON o.oid = operator_id JOIN pg_catalog.pg_am am ON am.oid = o.opcmethod
                            WHERE NOT o.opcdefault OR am.amname <> 'btree')
                        AND NOT EXISTS (SELECT 1 FROM unnest(i.indcollation::oid[]) WITH ORDINALITY col(value,ord)
                            JOIN unnest(i.indkey::smallint[]) WITH ORDINALITY k(attnum,ord) ON k.ord = col.ord
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum
                            WHERE col.value <> a.attcollation)
                        AND {(index.Filter is null ? "i.indpred IS NULL" : "pg_catalog.pg_get_expr(i.indpred,i.indrelid) = " + Text(source.Type == typeof(Explore.Domain.SupportAccessSession) ? $"((status_id = {((int)Explore.Domain.Enums.SupportAccessSessionStatusEnum.Active).ToString(System.Globalization.CultureInfo.InvariantCulture)}) AND (ended_at_utc IS NULL))" : "(" + index.Filter + ")"))}
                        AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id ORDER BY k.ord)
                            = {keyColumns(index.Properties, source.Type)})
                    THEN RAISE EXCEPTION 'support_administration_source_key_drift' USING ERRCODE = '55000'; END IF;
                    """));
            string foreignKeys = string.Join("\n", entity.GetForeignKeys().Select(key =>
            {
                var principal = key.PrincipalEntityType;
                string principalTable = sql.Name(principal.GetTableName()!);
                return $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint fk
                        WHERE fk.contype = 'f' AND fk.convalidated AND NOT fk.condeferrable AND NOT fk.condeferred
                            AND fk.confmatchtype = 's' AND fk.confupdtype = 'a'
                            AND fk.confdeltype = '{(key.DeleteBehavior == DeleteBehavior.Cascade ? "c"
                                : key.DeleteBehavior == DeleteBehavior.SetNull ? "n" : key.DeleteBehavior == DeleteBehavior.Restrict ? "r" : "a")}'
                            AND fk.conrelid = pg_catalog.to_regclass({table}) AND fk.confrelid = pg_catalog.to_regclass({Text(principalTable)})
                            AND ARRAY(SELECT a.attname FROM unnest(fk.conkey) WITH ORDINALITY k(id,ord)
                                JOIN pg_catalog.pg_attribute a ON a.attrelid = fk.conrelid AND a.attnum = k.id ORDER BY k.ord)
                                = {keyColumns(key.Properties, source.Type)}
                            AND ARRAY(SELECT a.attname FROM unnest(fk.confkey) WITH ORDINALITY k(id,ord)
                                JOIN pg_catalog.pg_attribute a ON a.attrelid = fk.confrelid AND a.attnum = k.id ORDER BY k.ord)
                                = {keyColumns(key.PrincipalKey.Properties, principal.ClrType)}
                            AND EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgconstraint = fk.oid)
                            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                                WHERE t.tgconstraint = fk.oid AND t.tgenabled NOT IN ('O','A')))
                    THEN RAISE EXCEPTION 'support_administration_source_fk_drift' USING ERRCODE = '55000'; END IF;
                    """;
            }));
            (string Name, string Expression)[] checks = source.Type == typeof(Explore.Domain.SupportAccessSession)
                ? [
                    ("ck_support_access_sessions_timebox", "(expires_at_utc > started_at_utc)"),
                    ("ck_support_access_sessions_end_after_start", "((ended_at_utc IS NULL) OR (ended_at_utc >= started_at_utc))"),
                    ("ck_support_access_sessions_terminal_reason", "((end_reason_id IS NULL) = (ended_at_utc IS NULL))")
                ]
                : source.Type == typeof(Explore.Domain.TenantUser)
                    ? [("ck_tenant_users_status", "(status_id = ANY (ARRAY[1, 2, 3, 4]))")] : [];
            string checkGuards = string.Join("\n", checks.Select(check => $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c
                    WHERE c.conrelid = pg_catalog.to_regclass({table}) AND c.contype = 'c'
                        AND c.conname = {Text(check.Name)} AND c.convalidated AND NOT c.connoinherit
                        AND pg_catalog.pg_get_expr(c.conbin,c.conrelid) = {Text(check.Expression)})
                THEN RAISE EXCEPTION 'support_administration_source_check_drift' USING ERRCODE = '55000'; END IF;
                """));
            string mutationTriggers = !Mutable(source.Type) ? "" : $"""
                {(source.Type == typeof(Explore.Domain.SupportAccessSession)
                    ? PostgresMixedSettingsContract.ProvisioningSharedWriter(context, enrollment)
                        .AssertInstalledOrMigrationAbsent(context, enrollment) : "")}
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                    WHERE t.tgrelid = pg_catalog.to_regclass({table}) AND NOT t.tgisinternal
                        AND t.tgname <> '{Prefix}changed'
                        {(source.Type == typeof(Explore.Domain.SupportAccessSession)
                            ? $"""
                                AND NOT (t.tgname = 'zz_human_support_access_source_write' AND t.tgfoid = pg_catalog.to_regprocedure({Text(sql.Name("human_support_access_source_write") + "()")}))
                                AND NOT (t.tgname = 'mixed_settings_changed' AND t.tgfoid = pg_catalog.to_regprocedure({Text(sql.Name("mixed_settings_changed") + "()")}))
                                """
                            : "")})
                THEN RAISE EXCEPTION 'support_administration_unexpected_effect_trigger' USING ERRCODE = '55000'; END IF;
                """;
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relkind = 'r' AND t.relpersistence = 'p'
                    AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid
                    AND t.relrowsecurity = {tenant.ToString().ToLowerInvariant()} AND t.relforcerowsecurity = {tenant.ToString().ToLowerInvariant()})
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({table}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i
                        WHERE i.inhrelid = pg_catalog.to_regclass({table}) OR i.inhparent = pg_catalog.to_regclass({table}))
                    OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                        AND a.attnum > 0 AND NOT a.attisdropped) <> {source.Properties.Length}
                    OR (SELECT count(*) FROM pg_catalog.pg_index i WHERE i.indrelid = pg_catalog.to_regclass({table})
                        AND i.indisunique) <> {entity.GetKeys().Count() + entity.GetIndexes().Count(index => index.IsUnique)}
                    OR (SELECT count(*) FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({table})
                        AND c.contype = 'f') <> {entity.GetForeignKeys().Count()}
                    OR (SELECT count(*) FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({table})
                        AND c.contype = 'c') <> {checks.Length}
                    OR {columns}
                THEN RAISE EXCEPTION 'support_administration_source_catalog_drift' USING ERRCODE = '55000'; END IF;
                {keys}
                {foreignKeys}
                {checkGuards}
                {mutationTriggers}
                """;
        }));
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string roles = string.Join(",", Roles.Select(Text));
        string routineIds = string.Join(",", Routines.Select(routine =>
            "pg_catalog.to_regprocedure(" + Text(FunctionSql(context, routine.Name) + "(" + routine.Signature + ")") + ")"));
        string routines = string.Join("\n", Routines.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Name) + "(" + routine.Signature + ")")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                    AND NOT f.proretset AND f.proargdefaults IS NULL AND f.prorettype = 'pg_catalog.{routine.Result}'::regtype
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.obj_description(f.oid,'pg_proc') =
                        '{Stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex'))
            THEN RAISE EXCEPTION 'support_administration_routine_drift' USING ERRCODE = '55000'; END IF;
            """));
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string policies = string.Join("\n", Inventory.Select(source =>
        {
            string table = Text(sql.Table(source.Type));
            var tenant = tenantTables.SingleOrDefault(candidate =>
                candidate.TableName == context.Model.FindEntityType(source.Type)!.GetTableName());
            string expected = tenant is null ? "" : $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {table}::regclass
                    AND p.polname = '{Policy}' AND p.polcmd = '*' AND p.polpermissive
                    AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                    AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true'
                    AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid) = 'true')
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {table}::regclass
                        AND p.polname = '{PostgresTenantRowLevelSecurityModel.PolicyName}' AND p.polpermissive
                        AND p.polcmd = '*' AND p.polroles = ARRAY[0::oid]
                        AND pg_catalog.obj_description(p.oid,'pg_policy') =
                            {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, sql.Schema))}
                            || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                THEN RAISE EXCEPTION 'support_administration_source_policy_missing' USING ERRCODE = '55000'; END IF;
                """;
            return expected + $"""
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {table}::regclass
                    {(tenant is null ? "" : $"AND p.polname NOT IN ('{Policy}','{PostgresTenantRowLevelSecurityModel.PolicyName}')")}
                    AND (0::oid = ANY(p.polroles) OR EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                        WHERE pg_catalog.pg_has_role('{OwnerRole}',role_id,'USAGE'))))
                THEN RAISE EXCEPTION 'support_administration_source_policy_widened' USING ERRCODE = '55000'; END IF;
                """;
        }));
        string triggers = string.Join("\n", Inventory.Select(source => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                WHERE t.tgrelid = {Text(sql.Table(source.Type))}::regclass AND t.tgname = '{Prefix}changed'
                    AND t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, "changed") + "()")})
                    AND t.tgtype = 62 AND t.tgenabled = 'A' AND NOT t.tgisinternal AND t.tgqual IS NULL
                    AND t.tgnargs = 0 AND t.tgargs = ''::bytea AND t.tgattr = ''::int2vector AND t.tgconstraint = 0
                    AND NOT t.tgdeferrable AND NOT t.tginitdeferred AND t.tgoldtable IS NULL AND t.tgnewtable IS NULL)
            THEN RAISE EXCEPTION 'support_administration_writer_drift' USING ERRCODE = '55000'; END IF;
            """));
        string callerDenial = string.Join("\n", Inventory.Select(source =>
        {
            string scalarAuthority = $"pg_catalog.has_any_column_privilege({Text(enrollment.RuntimeLogin)},{Text(sql.Table(source.Type))}::regclass,'SELECT,INSERT,UPDATE,REFERENCES')";
            string scalarDenial = source.Type == typeof(Explore.Domain.Tenant)
                ? $"({scalarAuthority}) AND NOT COALESCE(({PostgresTenantApiKeyStoragePermissions.CompleteManifest(context, enrollment)}),false)"
                : scalarAuthority;
            return $"""
            IF pg_catalog.has_table_privilege({Text(enrollment.RuntimeLogin)},{Text(sql.Table(source.Type))}::regclass,
                    'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                OR ({scalarDenial})
            THEN RAISE EXCEPTION 'support_administration_ambient_scalar_authority' USING ERRCODE = '55000'; END IF;
            """;
        }));
        return $"""
            {SourceGuard(context, enrollment)}
            {OriginalSources(context, enrollment)}
            {PostgresManagedProviderProvisioningContract.SharedWriterAssertionSql(context, enrollment)}
            {PostgresPrivilegeGuard.BuildAssertSql(Privileges(context, enrollment))}
            {routines}
            {policies}
            {triggers}
            {callerDenial}
            {AttemptSql(context).PrivateCatalog(enrollment.MigratorLogin)}
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Schema)}
                    AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                    AND pg_catalog.shobj_description(r.oid,'pg_authid') IS DISTINCT FROM '{Stamp}role:' || r.rolname)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                    AND NOT pg_catalog.has_schema_privilege(r.oid,{Text(sql.Schema)},'USAGE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND t.relkind = 'S' AND pg_catalog.has_sequence_privilege(r.oid,t.oid,'USAGE,SELECT,UPDATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_database d CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND pg_catalog.has_database_privilege(r.oid,d.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
                    CROSS JOIN pg_catalog.pg_roles r WHERE r.rolname IN ({roles}) AND n.nspname <> {Text(sql.Schema)}
                        AND n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%'
                        AND t.relkind IN ('r','p','v','m','f')
                        AND (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_shdepend d WHERE d.refclassid = 'pg_catalog.pg_authid'::regclass
                    AND d.refobjid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})) AND d.deptype = 'o'
                    AND (d.classid <> 'pg_catalog.pg_proc'::regclass OR d.dbid <> (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database())
                        OR d.objid NOT IN ({routineIds})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid NOT IN ({routineIds})
                    AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
            THEN RAISE EXCEPTION 'support_administration_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        string.Join("\n", Functions(context, enrollment).Select(function => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, function.Name) + "(" + function.Signature + ")")})
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(function.Body))})
            THEN RAISE EXCEPTION 'support_administration_exact_body_drift' USING ERRCODE = '55000'; END IF;
            """));
}
