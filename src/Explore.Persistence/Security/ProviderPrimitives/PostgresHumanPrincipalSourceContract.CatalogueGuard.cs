using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresHumanPrincipalSourceContract
{
    private const string CatalogueGuardName = "human_principal_source_assert_catalogue";

    // Stored entries run as OwnerRole. Independent callers retain the inline catalogue
    // assertion and exact body checks because they cannot execute this private helper.
    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        PostgresContractAttestationSql.BuildGuardInvocationSql(
            CatalogueGuardWitness(context, enrollment),
            new HumanPrincipalSourceSql(context).Name(CatalogueGuardName));

    private static string CatalogueGuardWitness(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        PostgresContractAttestationSql.BuildCatalogueGuardWitnessSql(
            new HumanPrincipalSourceSql(context).Name(CatalogueGuardName),
            OwnerRole,
            Hash(CatalogueGuardBody(context, enrollment)),
            Stamp,
            "human_principal_catalogue_guard_drift");

    private static string CatalogueGuardBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        PostgresContractAttestationSql.BuildCatalogueGuardBodySql(CatalogueGuard(context, enrollment));

    private static string CatalogueGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string roles = string.Join(",", Roles.Select(Text));
        (string Name, string Signature, string Result)[] routines =
            [(Observe, Signature, "jsonb"), (Acquire, Signature, "jsonb"),
                (Session, SessionSignature, "jsonb"), (Write, "", "trigger"), (SettingWrite, "", "trigger"),
                (CatalogueGuardName, "", "void")];
        string routineIds = PostgresContractAttestationSql.RoutineSignatureIdsSql(
            context, routines, routine => (routine.Name, routine.Signature), (c, name) => new HumanPrincipalSourceSql(c).Name(name));
        string routineChecks = PostgresContractAttestationSql.BuildRoutineMetadataChecksSql(
            context, routines, routine => routine, (c, name) => new HumanPrincipalSourceSql(c).Name(name),
            Stamp, "human_principal_routine_drift");
        var tenantTables = context.GetTenantTableInventory();
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
                OR {PostgresContractAttestationSql.BuildAuthorityRoleAndNamespaceHygieneConditions(
                    sql.Schema, null, roles, Stamp, routineIds, checkNamespaceOwner: false)}
            THEN RAISE EXCEPTION 'human_principal_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }
}
