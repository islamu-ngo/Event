using Microsoft.EntityFrameworkCore;
using Model = Explore.Persistence.Security.ManagedProviderProvisioningDataModel;

namespace Explore.Persistence.Security;

public static partial class PostgresManagedProviderProvisioningContract
{
    public const string OwnerRole = "event_human_provisioning_owner";
    public const string MigratorRole = "event_human_provisioning_migrator";
    public const string GlobalRole = "event_human_provisioning_global";
    public const string SourceTable = "human_provisioning_source";
    private const string AttemptTable = "human_provisioning_attempt";
    private const string Prefix = "human_provisioning_";
    private const string Policy = "human_provisioning_data";
    private const string Stamp = "human-provisioning:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, GlobalRole];
    private static IEnumerable<Model.Relation> Sources => Model.Inventory.Concat(Model.CapacitySources);
    private static readonly (string Name, string Signature, string Arguments, string Result, bool Public)[] Routines =
    [
        ("read", "jsonb", "p_request jsonb", "jsonb", true),
        ("acquire", "jsonb,jsonb", "p_request jsonb,p_before jsonb", "jsonb", true),
        ("compile", "jsonb", "p_request jsonb", "jsonb", true),
        ("program", "jsonb,jsonb", "p_request jsonb,image jsonb", "jsonb", false),
        ("apply", "jsonb", "p_request jsonb", "jsonb", true),
        ("complete", "jsonb", "p_request jsonb", "jsonb", true),
        ("changed", "", "", "trigger", false),
        ("settlement", "", "", "trigger", false)
    ];

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        Model.RequireInventory(context);
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.GlobalLogin, enrollment.RuntimeLogin,
                enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(Roles.Contains))
            throw new InvalidOperationException("human_provisioning_enrollment_collision");
        var sql = new HumanPrincipalSourceSql(context);
        return new(sql.Schema, Roles,
            Routines.Select(routine => new PostgresPrivilegeGuard.Routine(Prefix + routine.Name, routine.Signature,
                OwnerRole, routine.Public ? [GlobalRole] : [OwnerRole])).ToArray(),
            [
                .. Sources.Select(relation => new PostgresPrivilegeGuard.Relation(
                    context.Model.FindEntityType(relation.Type)!.GetTableName()!, OwnerRole, [],
                    relation.Columns.Select(column => new PostgresPrivilegeGuard.Column(sql.ColumnName(relation.Type, column),
                        ["SELECT", .. relation.Insert ? new[] { "INSERT" } : [],
                            .. relation.Update.Contains(column, StringComparer.Ordinal) ? new[] { "UPDATE" } : []])).ToArray())),
                new(SourceTable, OwnerRole, [], [new("id", ["SELECT"]), new("revision", ["SELECT", "UPDATE"])]),
                new(AttemptTable, OwnerRole, ["SELECT", "INSERT", "UPDATE"], [])
            ],
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)]);
    }

    /// <summary>
    /// Source-owned composition assertion for sibling DATA contracts sharing these
    /// tables. Parent integration must allow this exact trigger, never arbitrary writers.
    /// </summary>
    public static string WriterAssertionSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        ProvisioningSharedWriter(context, enrollment).AssertInstalled(context);

    private static string ChangedBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            BEGIN
                IF current_user <> '{OwnerRole}' OR TG_WHEN <> 'BEFORE' OR TG_LEVEL <> 'STATEMENT'
                    OR TG_OP NOT IN ('INSERT','UPDATE','DELETE','TRUNCATE')
                    OR TG_RELID NOT IN ({string.Join(",", Sources.Select(source => Text(sql.Table(source.Type)) + "::regclass"))})
                THEN RAISE EXCEPTION 'human_provisioning_writer_mismatch' USING ERRCODE = '55000'; END IF;
                IF EXISTS (SELECT 1 FROM {sql.Name(AttemptTable)}
                    WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid() AND completed)
                THEN RAISE EXCEPTION 'human_provisioning_write_after_seal' USING ERRCODE = '42501'; END IF;
                UPDATE {sql.Name(SourceTable)} SET revision = revision + 1 WHERE id = 1;
                IF NOT FOUND THEN RAISE EXCEPTION 'human_provisioning_epoch_missing' USING ERRCODE = '55000'; END IF;
                RETURN NULL;
            END;
            """;
    }

    internal static ProvisioningSharedWriterAssertions.Writer ProvisioningSharedWriter(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        new(Prefix + "changed", Prefix + "changed", SourceTable, Stamp,
            Sources.Select(source => (source.Type, 62)).ToArray(),
            Privileges(context, enrollment), ChangedBody(context, enrollment));

    // This is a leaf: never call Guard, SourceCatalog, OriginalSources or another
    // owner's body builder here. Sibling writer bodies embed this exact assertion.
    // Independent Identity omits this co-located protocol. Whole absence is not
    // a reverse dependency; any installed footprint still requires the exact writer.
    internal static string SharedWriterAssertionSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) => $"""
        IF {ProvisioningSharedWriter(context, enrollment).Presence(context)} THEN
            {WriterAssertionSql(context, enrollment)}
        END IF;
        """;
}
