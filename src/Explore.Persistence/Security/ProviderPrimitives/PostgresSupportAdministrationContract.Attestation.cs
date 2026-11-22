using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresSupportAdministrationContract
{
    private static readonly string[] SingleInsertPrivilege = ["INSERT"];
    private static readonly string[] SingleUpdatePrivilege = ["UPDATE"];

    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        RequireInventory(context);
        var sql = new HumanPrincipalSourceSql(context);
        return PostgresContractAttestationSql.BuildAdministrationContractPrivileges(
            context,
            enrollment,
            Roles,
            Routines.Select(routine => (routine.Name, routine.Signature, routine.Result)),
            Prefix,
            Inventory.Select(source => (source.Type, source.Properties.Select(property =>
                new PostgresPrivilegeGuard.Column(sql.ColumnName(source.Type, property),
                    ["SELECT", .. Mutable(source.Type) ? SingleInsertPrivilege : [], .. UpdateColumns(source.Type).Contains(property, StringComparer.Ordinal)
                        ? SingleUpdatePrivilege : []])).ToArray())),
            OwnerRole,
            MigratorRole,
            RuntimeRole,
            SourceTable,
            AttemptTable,
            "support_administration_enrollment_collision");
    }

    private static string OriginalSources(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        PostgresContractAttestationSql.OriginalAdministrationSourcesSql(context, enrollment);

    private static string SourceGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        RequireInventory(context);
        var sql = new HumanPrincipalSourceSql(context);
        var tenantTables = context.GetTenantTableInventory();
        return string.Join("\n", Inventory.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            string table = Text(sql.Table(source.Type));
            bool tenant = tenantTables.Any(candidate => candidate.TableName == entity.GetTableName());
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
            string extraCheckCountPredicate = $"""
                (SELECT count(*) FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({table})
                    AND c.contype = 'c') <> {checks.Length}
                """;
            string trailing = checkGuards + (string.IsNullOrEmpty(mutationTriggers) ? string.Empty : "\n" + mutationTriggers);
            return PostgresContractAttestationSql.BuildRelationSourceCatalogSql(
                context,
                sql,
                source.Type,
                source.Properties,
                enrollment,
                tenant,
                name =>
                {
                    string? expectedDefault = source.Type == typeof(Explore.Domain.TenantUser) ? name switch
                    {
                        "StatusId" => ((int)Explore.Domain.Enums.TenantUserStatusEnum.Active).ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "CreatedAt" => "now()",
                        _ => null
                    } : null;
                    return expectedDefault is null ? "NOT a.atthasdef" : $"""
                        a.atthasdef AND EXISTS (SELECT 1 FROM pg_catalog.pg_attrdef d
                            WHERE d.adrelid = a.attrelid AND d.adnum = a.attnum
                                AND pg_catalog.pg_get_expr(d.adbin,d.adrelid) = {Text(expectedDefault)})
                        """;
                },
                (type, filter) => type == typeof(Explore.Domain.SupportAccessSession)
                    ? $"((status_id = {((int)Explore.Domain.Enums.SupportAccessSessionStatusEnum.Active).ToString(System.Globalization.CultureInfo.InvariantCulture)}) AND (ended_at_utc IS NULL))"
                    : "(" + filter + ")",
                "support_administration_source_key_drift",
                "support_administration_source_fk_drift",
                "support_administration_source_catalog_drift",
                extraCheckCountPredicate,
                trailing);
        }));
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string roles = string.Join(",", Roles.Select(Text));
        string routineIds = PostgresContractAttestationSql.RoutineSignatureIdsSql(
            context, Routines, routine => (routine.Name, routine.Signature), FunctionSql);
        string routines = PostgresContractAttestationSql.BuildRoutineMetadataChecksSql(
            context, Routines, routine => (routine.Name, routine.Signature, routine.Result),
            FunctionSql, Stamp, "support_administration_routine_drift");
        string policies = PostgresContractAttestationSql.BuildSourcePolicyGuardsSql(
            context, sql, Inventory.Select(source => source.Type), Policy, OwnerRole,
            "support_administration_source_policy_missing", "support_administration_source_policy_widened");
        string triggers = PostgresContractAttestationSql.BuildChangedTriggerGuardsSql(
            sql, Inventory.Select(source => source.Type), $"{Prefix}changed",
            FunctionSql(context, "changed") + "()", "support_administration_writer_drift");
        string callerDenial = string.Join("\n", Inventory.Select(source =>
        {
            // Only actual intersecting sources recognize a complete fixed storage
            // owner; private support relations retain zero scalar authority.
            string scalarAuthority = $"pg_catalog.has_any_column_privilege({Text(enrollment.RuntimeLogin)},{Text(sql.Table(source.Type))}::regclass,'SELECT,INSERT,UPDATE,REFERENCES')";
            string scalarDenial = PostgresTenantApiKeyStoragePermissions.RuntimeParentReadColumns(context, source.Type).Count != 0
                ? $"({scalarAuthority}) AND NOT COALESCE(({PostgresTenantApiKeyStoragePermissions.CompleteManifest(context, enrollment)}),false)"
                : PostgresTenantMembershipMutationPermissions.TryGetRuntimeColumns(context, source.Type, out _, out _, out _)
                    ? $"NOT COALESCE(({PostgresTenantMembershipMutationPermissions.OptionalCataloguePredicate(context, enrollment)}),false)"
                    : scalarAuthority;
            if (source.Type == typeof(Explore.Domain.TenantUser))
            {
                string actorScalarDenial = $"""
                    EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r
                        WHERE t.oid={Text(sql.Table(source.Type))}::regclass
                            AND pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'MEMBER')
                            AND (r.oid=t.relowner OR r.rolsuper OR r.rolbypassrls OR r.rolcreaterole OR r.rolcreatedb OR r.rolreplication
                                OR (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                                    OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", false)})
                                OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                    CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                                    WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                                        AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                            OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege)
                                                AND NOT COALESCE(({PostgresActorOwnershipReadPermissions.RuntimeReadPredicate(context, source.Type,
                                                    enrollment.RuntimeLogin, "r.oid", "t.oid", "a.attname", "permission.privilege")}),false))))))
                    """;
                scalarDenial = $"({scalarDenial}) AND ({actorScalarDenial})";
            }
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
            IF {PostgresContractAttestationSql.BuildAuthorityRoleAndNamespaceHygieneConditions(
                sql.Schema, enrollment.MigratorLogin, roles, Stamp, routineIds, checkNamespaceOwner: true)}
            THEN RAISE EXCEPTION 'support_administration_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        PostgresContractAttestationSql.BuildExactBodiesSql(
            context,
            Functions(context, enrollment),
            function => (function.Name, function.Signature, function.Body),
            FunctionSql,
            "support_administration_exact_body_drift");
}
