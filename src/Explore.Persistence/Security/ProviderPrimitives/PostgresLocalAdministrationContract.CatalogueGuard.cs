using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresLocalAdministrationContract
{
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
                    ["SELECT", "INSERT", .. UpdateColumns(source.Type).Contains(property, StringComparer.Ordinal)
                        ? SingleUpdatePrivilege : []])).ToArray())),
            OwnerRole,
            MigratorRole,
            RuntimeRole,
            SourceTable,
            AttemptTable,
            "local_administration_enrollment_collision");
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
            bool tenant = tenantTables.Any(candidate => candidate.TableName == entity.GetTableName());
            return PostgresContractAttestationSql.BuildRelationSourceCatalogSql(
                context,
                sql,
                source.Type,
                source.MappedProperties,
                enrollment,
                tenant,
                // The generated source-owner migration retains this zero default; it is not a new PII grant.
                name => source.Type == typeof(Explore.Domain.UserPii) && name == "_birthDateRevision"
                    ? "a.atthasdef AND EXISTS (SELECT 1 FROM pg_catalog.pg_attrdef d WHERE d.adrelid = a.attrelid AND d.adnum = a.attnum AND pg_catalog.pg_get_expr(d.adbin,d.adrelid) IN ('0','''0''::bigint'))"
                    : "NOT a.atthasdef",
                (_, filter) => "(" + filter + ")",
                "local_administration_source_key_drift",
                "local_administration_source_fk_drift",
                "local_administration_source_catalog_drift");
        }));
    }

    private static string CatalogueGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string roles = string.Join(",", Roles.Select(Text));
        string routineIds = PostgresContractAttestationSql.RoutineSignatureIdsSql(
            context, Routines, routine => (routine.Name, routine.Signature), FunctionSql);
        string routines = PostgresContractAttestationSql.BuildRoutineMetadataChecksSql(
            context, Routines, routine => (routine.Name, routine.Signature, routine.Result),
            FunctionSql, Stamp, "local_administration_routine_drift");
        string policies = PostgresContractAttestationSql.BuildSourcePolicyGuardsSql(
            context, sql, Inventory.Select(source => source.Type), Policy, OwnerRole,
            "local_administration_source_policy_missing", "local_administration_source_policy_widened");
        string triggers = PostgresContractAttestationSql.BuildChangedTriggerGuardsSql(
            sql, Inventory.Select(source => source.Type), $"{Prefix}changed",
            FunctionSql(context, "changed") + "()", "local_administration_writer_drift");
        string callerDenial = $"""
            IF ({PostgresExternalTenantEnrollmentPermissions.OptionalCataloguePredicate(context, enrollment)}) IS NOT TRUE
            THEN RAISE EXCEPTION 'local_administration_ambient_scalar_authority' USING ERRCODE = '55000'; END IF;
            """ + "\n" + string.Join("\n", Inventory
            .Where(source => !PostgresExternalTenantEnrollmentPermissions.Includes(source.Type))
            .Select(source => $"""
            IF pg_catalog.has_table_privilege({Text(enrollment.RuntimeLogin)},{Text(sql.Table(source.Type))}::regclass,
                    'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                OR pg_catalog.has_any_column_privilege({Text(enrollment.RuntimeLogin)},{Text(sql.Table(source.Type))}::regclass,
                    'SELECT,INSERT,UPDATE,REFERENCES')
            THEN RAISE EXCEPTION 'local_administration_ambient_scalar_authority' USING ERRCODE = '55000'; END IF;
            """));
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
            THEN RAISE EXCEPTION 'local_administration_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }
}
