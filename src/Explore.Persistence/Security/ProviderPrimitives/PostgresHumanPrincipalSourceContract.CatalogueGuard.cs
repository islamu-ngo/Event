using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresHumanPrincipalSourceContract
{
    private const string CatalogueGuardName = "human_principal_source_assert_catalogue";

    // Stored entries run as OwnerRole. Independent callers retain the inline catalogue
    // assertion and exact body checks because they cannot execute this private helper.
    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) => $"""
        {CatalogueGuardWitness(context, enrollment)}
        PERFORM {new HumanPrincipalSourceSql(context).Name(CatalogueGuardName)}();
        """;

    private static string CatalogueGuardWitness(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        string function = new HumanPrincipalSourceSql(context).Name(CatalogueGuardName);
        string fingerprint = Hash(CatalogueGuardBody(context, enrollment));
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(function + "()")})
                    AND f.proowner = pg_catalog.to_regrole('{OwnerRole}')::oid
                    AND f.prokind = 'f' AND f.prosecdef AND f.provolatile = 'v' AND f.proparallel = 'u'
                    AND NOT f.proleakproof AND NOT f.proisstrict AND NOT f.proretset
                    AND f.prorettype = 'pg_catalog.void'::regtype AND f.pronargs = 0
                    AND f.proargtypes = ''::pg_catalog.oidvector AND f.pronargdefaults = 0
                    AND f.proargdefaults IS NULL AND f.provariadic = 0 AND f.prosupport = 0
                    AND f.proallargtypes IS NULL AND f.proargmodes IS NULL AND f.proargnames IS NULL
                    AND f.proconfig = ARRAY['search_path=pg_catalog']::text[]
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(fingerprint)}
                    AND pg_catalog.obj_description(f.oid,'pg_proc') = {Text(Stamp + fingerprint)}
                    AND pg_catalog.has_function_privilege(f.proowner,f.oid,'EXECUTE')
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(
                        COALESCE(f.proacl,pg_catalog.acldefault('f',f.proowner))) a
                        WHERE a.privilege_type <> 'EXECUTE' OR a.grantee <> f.proowner))
            THEN RAISE EXCEPTION 'human_principal_catalogue_guard_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string CatalogueGuardBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) => $"""
        BEGIN
            {CatalogueGuard(context, enrollment)}
        END;
        """;

    private static string CatalogueGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string roles = string.Join(",", Roles.Select(Text));
        (string Name, string Signature, string Result)[] routines =
            [(Observe, Signature, "jsonb"), (Acquire, Signature, "jsonb"),
                (Session, SessionSignature, "jsonb"), (Write, "", "trigger"), (SettingWrite, "", "trigger"),
                (CatalogueGuardName, "", "void")];
        // Resolve the source-declared set in an uncorrelated subquery on every
        // assertion, rather than repeating name resolution for every catalog row.
        string routineIds = "SELECT pg_catalog.to_regprocedure(expected.signature) FROM (VALUES "
            + string.Join(",", routines.Select(routine =>
                $"({Text(sql.Name(routine.Name) + "(" + routine.Signature + ")")})"))
            + ") expected(signature)";
        string routineChecks = string.Join("\n", routines.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(sql.Name(routine.Name) + "(" + routine.Signature + ")")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof
                    AND NOT f.proisstrict AND NOT f.proretset AND f.proargdefaults IS NULL
                    AND f.prorettype = 'pg_catalog.{routine.Result}'::regtype
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.obj_description(f.oid,'pg_proc') =
                        '{Stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex'))
            THEN RAISE EXCEPTION 'human_principal_routine_drift' USING ERRCODE = '55000'; END IF;
            """));
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string policies = string.Join("\n", Sources.Select(source =>
        {
            string table = sql.Table(source.Type);
            var tenant = tenantTables.SingleOrDefault(candidate =>
                candidate.TableName == context.Model.FindEntityType(source.Type)!.GetTableName());
            string tenantGuard = tenant is null ? "" : $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({Text(table)})
                    AND t.relrowsecurity AND t.relforcerowsecurity)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = pg_catalog.to_regclass({Text(table)})
                            AND p.polname = '{PostgresTenantRowLevelSecurityModel.PolicyName}'
                            AND p.polpermissive AND p.polcmd = '*' AND p.polroles = ARRAY[0::oid]
                            AND pg_catalog.obj_description(p.oid,'pg_policy') =
                                {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, sql.Schema))}
                                || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                THEN RAISE EXCEPTION 'human_principal_tenant_policy_drift' USING ERRCODE = '55000'; END IF;
                """;
            return $"""
                {tenantGuard}
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = pg_catalog.to_regclass({Text(table)}) AND p.polname = '{ReadPolicy}'
                        AND p.polpermissive AND p.polcmd = 'r' AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true' AND p.polwithcheck IS NULL)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = pg_catalog.to_regclass({Text(table)}) AND p.polname = '{UpdatePolicy}'
                            AND p.polpermissive AND p.polcmd = 'w' AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                            AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true'
                            AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid) = 'true')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = pg_catalog.to_regclass({Text(table)})
                            AND (0::oid = ANY(p.polroles) OR pg_catalog.to_regrole('{OwnerRole}')::oid = ANY(p.polroles))
                            AND p.polname NOT IN ('{ReadPolicy}','{UpdatePolicy}'
                                {(tenant is null ? "" : $",'{PostgresTenantRowLevelSecurityModel.PolicyName}'")}))
                THEN RAISE EXCEPTION 'human_principal_rls_drift' USING ERRCODE = '55000'; END IF;
                """;
        }));
        string triggerChecks = string.Join("\n", Sources.Where(source => source.Type != typeof(RolePermission)).Select(source =>
        {
            string table = sql.Table(source.Type);
            bool setting = source.Type == typeof(SystemSetting);
            string Check(string name, string function, int kind) => $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                    WHERE t.tgrelid = pg_catalog.to_regclass({Text(table)}) AND t.tgname = '{name}'
                        AND t.tgfoid = pg_catalog.to_regprocedure({Text(sql.Name(function) + "()")})
                        AND t.tgtype = {kind} AND t.tgenabled = 'A' AND NOT t.tgisinternal
                        AND t.tgnargs = 0 AND t.tgqual IS NULL AND t.tgattr = ''::int2vector
                        AND t.tgoldtable IS NULL AND t.tgnewtable IS NULL)
                THEN RAISE EXCEPTION 'human_principal_writer_drift' USING ERRCODE = '55000'; END IF;
                """;
            return setting ? Check(SettingWrite, SettingWrite, 29) + Check(Write, Write, 34)
                : Check(Write, Write, 62);
        }));
        string anchor = sql.Name(Anchor);
        return $"""
            {PostgresManagedProviderProvisioningContract.SharedWriterAssertionSql(context, enrollment)}
            {PostgresPrivilegeGuard.BuildAssertSql(Privileges(context, enrollment))}
            {SourceGuard(context, enrollment)}
            {routineChecks}
            {policies}
            {triggerChecks}
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                WHERE t.oid = pg_catalog.to_regclass({Text(anchor)}) AND t.relkind = 'r'
                    AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid
                    AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity)
                OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({Text(anchor)})
                    AND a.attnum > 0 AND NOT a.attisdropped) <> 2
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({Text(anchor)})
                    AND a.attname = 'id' AND a.atttypid = 'smallint'::regtype AND a.attnotnull)
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({Text(anchor)})
                    AND a.attname = 'revision' AND a.atttypid = 'boolean'::regtype AND a.attnotnull)
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({Text(anchor)})
                    AND c.contype = 'p' AND c.convalidated AND NOT c.condeferrable
                    AND pg_catalog.pg_get_constraintdef(c.oid) = 'PRIMARY KEY (id)')
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({Text(anchor)})
                    AND c.contype = 'c' AND c.convalidated AND pg_catalog.pg_get_expr(c.conbin,c.conrelid) = '(id = 1)')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = pg_catalog.to_regclass({Text(anchor)}) AND NOT t.tgisinternal)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({Text(anchor)}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
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
                        OR d.objid NOT IN ({routineIds})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid NOT IN ({routineIds})
                    AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a
                            WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
            THEN RAISE EXCEPTION 'human_principal_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }
}
