using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Security;

public static partial class PostgresEmailDeliveryPolicyContract
{
    private static readonly string[] SelectPrivilege = ["SELECT"];
    private static readonly string[] UpdatePrivilege = ["UPDATE"];

    private const string RestrictedCallerSql = """
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles role WHERE role.rolname = session_user
            AND role.rolcanlogin AND NOT role.rolsuper AND NOT role.rolbypassrls
            AND NOT role.rolcreatedb AND NOT role.rolcreaterole AND NOT role.rolreplication) THEN
            RAISE EXCEPTION 'email_policy_caller_authority_invalid' USING ERRCODE = '42501';
        END IF;
        """;

    internal static PostgresPrivilegeGuard.Contract BuildPrivilegeContract(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        PostgresPrivilegeGuard.Relation Relation<T>(params PostgresPrivilegeGuard.Column[] columns) =>
            new(context.Model.FindEntityType(typeof(T))!.GetTableName()!, OwnerRole, [], columns);
        PostgresPrivilegeGuard.Relation RecipientRelation<T>(params PostgresPrivilegeGuard.Column[] columns) =>
            new(context.Model.FindEntityType(typeof(T))!.GetTableName()!, RecipientOwnerRole, [], columns);
        PostgresPrivilegeGuard.Column Column<T>(string property, params string[] privileges)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return new(entity.FindProperty(property)!.GetColumnName(store)!, privileges);
        }
        string[] overrideProperties =
        [
            nameof(TenantSetting.Id), nameof(TenantSetting.TenantId), nameof(TenantSetting.SettingKey),
            nameof(TenantSetting.Value), nameof(TenantSetting.IsLocked), nameof(TenantSetting.CreatedAt),
            nameof(TenantSetting.CreatedBy), nameof(TenantSetting.UpdatedAt), nameof(TenantSetting.UpdatedBy)
        ];
        string[] controlSelect =
        [
            nameof(EmailDispatchTenantControl.TenantId), nameof(EmailDispatchTenantControl.DeliveryPolicyRevision),
            nameof(EmailDispatchTenantControl.OptionalSuppressedThroughRevision),
            nameof(EmailDispatchTenantControl.OptionalSuppressedThroughUtc)
        ];
        string[] controlInsert =
        [
            nameof(EmailDispatchTenantControl.Id), .. controlSelect,
            nameof(EmailDispatchTenantControl.CreatedAt), nameof(EmailDispatchTenantControl.CreatedBy),
            nameof(EmailDispatchTenantControl.UpdatedAt), nameof(EmailDispatchTenantControl.UpdatedBy),
            nameof(EmailDispatchTenantControl.IsPaused)
        ];
        string[] controlUpdate =
        [
            nameof(EmailDispatchTenantControl.DeliveryPolicyRevision),
            nameof(EmailDispatchTenantControl.OptionalSuppressedThroughRevision),
            nameof(EmailDispatchTenantControl.OptionalSuppressedThroughUtc),
            nameof(EmailDispatchTenantControl.UpdatedAt), nameof(EmailDispatchTenantControl.UpdatedBy)
        ];
        var controlColumns = controlInsert.Select(property => Column<EmailDispatchTenantControl>(property,
            [
                .. (controlSelect.Contains(property, StringComparer.Ordinal) ? SelectPrivilege : []),
                "INSERT",
                .. (controlUpdate.Contains(property, StringComparer.Ordinal) ? UpdatePrivilege : [])
            ])).ToArray();
        return new(Schema(context),
            [OwnerRole, MigratorRole, RuntimeRole, GlobalRole, InstanceMutationRole, TenantMutationRole, RecipientOwnerRole],
            [
                new("email_delivery_policy_overrides", "", OwnerRole, [RuntimeRole, GlobalRole, InstanceMutationRole]),
                new("email_delivery_policy_controls_capture", "", OwnerRole, [OwnerRole]),
                new("email_delivery_policy_controls_processor_guard", "", OwnerRole, [OwnerRole]),
                new("email_delivery_policy_controls_require_mutation", "pg_catalog.uuid", OwnerRole,
                    [InstanceMutationRole, TenantMutationRole]),
                new("email_delivery_policy_controls", "pg_catalog.uuid[]", OwnerRole,
                    [InstanceMutationRole, TenantMutationRole]),
                new("email_delivery_policy_controls_apply", "pg_catalog.jsonb, pg_catalog.timestamptz, pg_catalog.uuid",
                    OwnerRole, [InstanceMutationRole, TenantMutationRole]),
                new("email_delivery_recipient", "pg_catalog.uuid", RecipientOwnerRole,
                    [RuntimeRole, GlobalRole, InstanceMutationRole])
            ],
            [
                Relation<TenantSetting>(overrideProperties.Select(property => Column<TenantSetting>(property, "SELECT")).ToArray()),
                Relation<Tenant>(Column<Tenant>(nameof(Tenant.Id), "SELECT")),
                Relation<EmailDispatchTenantControl>(controlColumns),
                new("email_delivery_policy_mutation_receipts", OwnerRole, ["SELECT", "INSERT", "UPDATE"], []),
                RecipientRelation<User>(
                    Column<User>(nameof(User.Id), "SELECT"),
                    Column<User>(nameof(User.IsDeleted), "SELECT")),
                RecipientRelation<UserPii>(
                    Column<UserPii>(nameof(UserPii.UserId), "SELECT"),
                    Column<UserPii>(nameof(UserPii.Email), "SELECT")),
                RecipientRelation<UserIdentityEmailClaim>(
                    Column<UserIdentityEmailClaim>(nameof(UserIdentityEmailClaim.Id), "SELECT"),
                    Column<UserIdentityEmailClaim>(nameof(UserIdentityEmailClaim.UserId), "SELECT"),
                    Column<UserIdentityEmailClaim>(nameof(UserIdentityEmailClaim.NormalizedEmail), "SELECT")),
                RecipientRelation<UserIdentityEmailEvidence>(
                    Column<UserIdentityEmailEvidence>(nameof(UserIdentityEmailEvidence.UserId), "SELECT"),
                    Column<UserIdentityEmailEvidence>(nameof(UserIdentityEmailEvidence.ClaimId), "SELECT"),
                    Column<UserIdentityEmailEvidence>(nameof(UserIdentityEmailEvidence.IsActive), "SELECT")),
                .. MutationPrivileges(context)
            ],
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(RecipientOwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false),
                new(InstanceMutationRole, enrollment.EmailInstanceMutationLogin, true, false, false),
                new(TenantMutationRole, enrollment.EmailTenantMutationLogin, true, false, false)
            ]);
    }

    internal static string PrivilegeAssertion(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) => $"""
        IF pg_catalog.to_regrole('{PostgresMixedSettingsContract.OwnerRole}') IS NULL THEN
            {PostgresPrivilegeGuard.BuildAssertSql(BuildPrivilegeContract(context, enrollment))}
        ELSE
            {PostgresPrivilegeGuard.BuildAssertSql(PostgresMixedSettingsContract.ComposedPrivileges(context, enrollment))}
        END IF;
        """;

    private static string LoginLiteral(string login) => "E'" + login.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";

    private static string LoginIdentifier(string login) => "\"" + login.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
