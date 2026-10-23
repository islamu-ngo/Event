using Explore.Domain;
using Explore.Persistence.Identity;

namespace Explore.Persistence.Security;

/// <summary>
/// Closed, source-owned composition, not caller-selected exceptions. The owner
/// supplies its original body and privilege contract. No live ACL supplies authority.
/// Assertions read catalogs only, including when composed by an execute-only caller.
/// </summary>
internal static partial class ProvisioningSharedWriterAssertions
{

    internal static Writer[] Inventory(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
    [
        PostgresHumanPrincipalSourceContract.ProvisioningSharedWriter(context, enrollment),
        PostgresLocalAdministrationContract.ProvisioningSharedWriter(context, enrollment),
        PostgresSupportAdministrationContract.ProvisioningSharedWriter(context, enrollment),
        PostgresHumanEventAssignmentSourceContract.ProvisioningSharedWriter(context, enrollment),
        PostgresHumanWebhookResourceSourceContract.ProvisioningSharedWriter(context, enrollment),
        PostgresStorageProbeSourceContract.ProvisioningSharedWriter(context, enrollment),
        PostgresProviderConfigurationContract.ProvisioningSharedWriter(context, enrollment),
        PostgresMixedSettingsContract.ProvisioningSharedWriter(context, enrollment),
        PostgresOrdinarySettingsContract.ProvisioningSharedWriter(context, enrollment, false),
        PostgresOrdinarySettingsContract.ProvisioningSharedWriter(context, enrollment, true),
        PostgresManagedProviderProvisioningContract.ProvisioningSharedWriter(context, enrollment)
    ];

    internal static (Writer[] Writers, string UndeclaredAssertion) TenantCreationInventory(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, IdentityDatabaseTopology topology)
    {
        if (!Enum.IsDefined(topology)) throw new ArgumentOutOfRangeException(nameof(topology));
        Writer[] always =
        [
            PostgresHumanPrincipalSourceContract.ProvisioningSharedWriter(context, enrollment),
            PostgresHumanEventAssignmentSourceContract.ProvisioningSharedWriter(context, enrollment),
            PostgresHumanWebhookResourceSourceContract.ProvisioningSharedWriter(context, enrollment),
            PostgresStorageProbeSourceContract.ProvisioningSharedWriter(context, enrollment),
            PostgresProviderConfigurationContract.ProvisioningSharedWriter(context, enrollment),
            PostgresMixedSettingsContract.ProvisioningSharedWriter(context, enrollment),
            PostgresOrdinarySettingsContract.ProvisioningSharedWriter(context, enrollment, false),
            PostgresOrdinarySettingsContract.ProvisioningSharedWriter(context, enrollment, true)
        ];
        // Select declared topology before constructing the co-located protocols.
        return topology == IdentityDatabaseTopology.Colocated
            ? ([.. always,
                PostgresSupportAdministrationContract.ProvisioningSharedWriter(context, enrollment),
                PostgresManagedProviderProvisioningContract.ProvisioningSharedWriter(context, enrollment)], "")
            : (always, PostgresSupportAdministrationContract.TenantCreationAbsentAssertion(context)
                + "\n" + PostgresManagedProviderProvisioningContract.TenantCreationAbsentAssertion(context));
    }

    internal static (Writer[] Writers, string UndeclaredAssertion) ManagedTenantWorkerCreationInventory(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, IdentityDatabaseTopology topology)
    {
        var creator = TenantCreationInventory(context, enrollment, topology);
        return topology == IdentityDatabaseTopology.Colocated
            ? ([.. creator.Writers, PostgresLocalAdministrationContract.ProvisioningSharedWriter(context, enrollment)],
                creator.UndeclaredAssertion)
            : (creator.Writers, creator.UndeclaredAssertion + "\n"
                + PostgresLocalAdministrationContract.TenantCreationAbsentAssertion(context));
    }

    internal static string AssertProtocolAbsent(ExploreDbContext context, string[] roles,
        string[] tables, IEnumerable<string> routines, string trigger)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN ({string.Join(",", roles.Select(Text))}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname={Text(sql.Schema)} AND c.relname IN ({string.Join(",", tables.Select(Text))}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid=f.pronamespace
                    WHERE n.nspname={Text(sql.Schema)} AND f.proname IN ({string.Join(",", routines.Select(Text))}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t JOIN pg_catalog.pg_class c ON c.oid=t.tgrelid
                    JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname={Text(sql.Schema)} AND t.tgname={Text(trigger)})
            THEN RAISE EXCEPTION 'tenant_creation_undeclared_identity_protocol' USING ERRCODE='55000'; END IF;
            """;
    }
}

// These bridges deliberately live in the owning partials: the target vocabulary,
// original body and declared enrollment remain private source-owned authorities.
public static partial class PostgresHumanPrincipalSourceContract
{
    internal static ProvisioningSharedWriterAssertions.Writer ProvisioningSharedWriter(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        new(Write, Write, Anchor, Stamp,
            Sources.Where(source => source.Type != typeof(RolePermission))
                .Select(source => (source.Type, source.Type == typeof(SystemSetting) ? 34 : 62)).ToArray(),
            Privileges(context, enrollment), WriteBody(context, enrollment, false), PrecedesProvisioning: true);
}

public static partial class PostgresLocalAdministrationContract
{
    internal static string TenantCreationAbsentAssertion(ExploreDbContext context) =>
        ProvisioningSharedWriterAssertions.AssertProtocolAbsent(context, Roles, [SourceTable, AttemptTable],
            Routines.Select(routine => Prefix + routine.Name), Prefix + "changed");

    internal static ProvisioningSharedWriterAssertions.Writer ProvisioningSharedWriter(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        new(Prefix + "changed", Prefix + "changed", SourceTable, Stamp,
            Inventory.Select(source => (source.Type, 62)).ToArray(),
            Privileges(context, enrollment), ChangedBody(context, enrollment), PrecedesProvisioning: true);
}

public static partial class PostgresSupportAdministrationContract
{
    internal static string TenantCreationAbsentAssertion(ExploreDbContext context) =>
        ProvisioningSharedWriterAssertions.AssertProtocolAbsent(context, Roles, [SourceTable, AttemptTable],
            Routines.Select(routine => Prefix + routine.Name), Prefix + "changed");

    internal static ProvisioningSharedWriterAssertions.Writer ProvisioningSharedWriter(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        new(Prefix + "changed", Prefix + "changed", SourceTable, Stamp,
            Inventory.Select(source => (source.Type, 62)).ToArray(),
            Privileges(context, enrollment), ChangedBody(context, enrollment), PrecedesProvisioning: true);
}

public static partial class PostgresManagedProviderProvisioningContract
{
    internal static string TenantCreationAbsentAssertion(ExploreDbContext context) =>
        ProvisioningSharedWriterAssertions.AssertProtocolAbsent(context, Roles, [SourceTable, AttemptTable],
            Routines.Select(routine => Prefix + routine.Name), Prefix + "changed");
}

public static partial class PostgresHumanEventAssignmentSourceContract
{
    internal static ProvisioningSharedWriterAssertions.Writer ProvisioningSharedWriter(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        new(WriterTrigger, Write, Anchor, Stamp,
            HumanEventAssignmentSourceSql.Sources.Select(source => (source.Type, 62)).ToArray(),
            Privileges(context, enrollment), WriteBody(context, enrollment), PrecedesProvisioning: true);
}

public static partial class PostgresHumanWebhookResourceSourceContract
{
    internal static ProvisioningSharedWriterAssertions.Writer ProvisioningSharedWriter(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        new(WriterTrigger, Write, Anchor, Stamp,
            HumanWebhookResourceSourceSql.Sources.Select(source => (source.Type, 62)).ToArray(),
            Privileges(context, enrollment), WriteBody(context, enrollment));
}

public static partial class PostgresStorageProbeSourceContract
{
    internal static ProvisioningSharedWriterAssertions.Writer ProvisioningSharedWriter(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        new(Prefix + "changed", Prefix + "changed", SourceTable, Stamp,
            Sources.Select(type => (type, 62)).ToArray(),
            Privileges(context, enrollment), ChangedBody(context, enrollment));
}

public static partial class PostgresProviderConfigurationContract
{
    internal static ProvisioningSharedWriterAssertions.Writer ProvisioningSharedWriter(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        new(Prefix + "changed", Prefix + "changed", SourceTable, Stamp,
            Sources.Select(type => (type, 62)).ToArray(),
            Privileges(context, enrollment), ChangedBody(context, enrollment));
}

public static partial class PostgresMixedSettingsContract
{
    internal static ProvisioningSharedWriterAssertions.Writer ProvisioningSharedWriter(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        new(Prefix + "changed", Prefix + "changed", SourceTable, Stamp,
            Sources(context).Select(source => (source.Type, 62)).ToArray(),
            Privileges(context, enrollment), ChangedBody(context, enrollment),
            AdmissionPrivileges: ComposedPrivileges(context, enrollment));
}

public static partial class PostgresOrdinarySettingsContract
{
    internal static ProvisioningSharedWriterAssertions.Writer ProvisioningSharedWriter(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool truncate) =>
        new(Prefix + (truncate ? "tenant_truncated" : "tenant_changed"),
            Prefix + (truncate ? "tenant_truncated" : "tenant_changed"), SourceTable, Stamp,
            [(typeof(Tenant), truncate ? 32 : 29)],
            Privileges(context, enrollment), TenantMembershipBody(context, enrollment, truncate));
}

