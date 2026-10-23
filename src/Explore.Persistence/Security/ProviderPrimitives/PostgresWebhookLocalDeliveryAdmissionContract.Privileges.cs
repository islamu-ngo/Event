using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Security;

public static partial class PostgresWebhookLocalDeliveryAdmissionContract
{
    private static PostgresPrivilegeGuard.Contract PrivilegeContract(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        PostgresPrivilegeGuard.Relation Relation<T>(string[] reads, string[] writes, string[] inserts)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return new(entity.GetTableName()!, OwnerRole, [],
                reads.Concat(writes).Concat(inserts).Distinct(StringComparer.Ordinal).Select(property =>
                    new PostgresPrivilegeGuard.Column(entity.FindProperty(property)!.GetColumnName(store)!,
                        (reads.Contains(property) ? new[] { "SELECT" } : [])
                        .Concat(writes.Contains(property) ? ["UPDATE"] : [])
                        .Concat(inserts.Contains(property) ? ["INSERT"] : []).ToArray())).ToArray());
        }
        var target = context.Model.FindEntityType(typeof(WebhookLocalTargetSnapshot))!;
        string name = target.GetTableName()!;
        return new(Schema(context, target), [OwnerRole, MigratorRole, GlobalRole],
            [
                new(name + "_due_tenants", "timestamp with time zone, integer", OwnerRole, [GlobalRole]),
                new(name + "_claim", "timestamp with time zone, interval, integer, integer, integer, uuid[], integer[], integer[], integer[], uuid", OwnerRole, [GlobalRole]),
                new(name + "_recover", "timestamp with time zone, text, integer", OwnerRole, [GlobalRole]),
                new(name + "_counts", "boolean, timestamp with time zone", OwnerRole, [GlobalRole]),
                new(name + "_system_governance", "", OwnerRole, [GlobalRole]),
                new(name + "_tenant_governance", "uuid", OwnerRole, [GlobalRole])
            ],
            [
                Relation<WebhookLocalTargetSnapshot>(
                    [nameof(WebhookLocalTargetSnapshot.Id), nameof(WebhookLocalTargetSnapshot.TenantId),
                        nameof(WebhookLocalTargetSnapshot.WebhookEndpointId), nameof(WebhookLocalTargetSnapshot.WebhookMessageId),
                        nameof(WebhookLocalTargetSnapshot.DeliveryStatusId), nameof(WebhookLocalTargetSnapshot.NextActionAtUtc),
                        nameof(WebhookLocalTargetSnapshot.CapturedAtUtc), nameof(WebhookLocalTargetSnapshot.ProcessingLeaseToken),
                        nameof(WebhookLocalTargetSnapshot.ProcessingLeaseExpiresAtUtc), nameof(WebhookLocalTargetSnapshot.DeliveryFence),
                        nameof(WebhookLocalTargetSnapshot.ConcurrencyVersion), nameof(WebhookLocalTargetSnapshot.MaxAttempts),
                        nameof(WebhookLocalTargetSnapshot.CreatedAt), nameof(WebhookLocalTargetSnapshot.UpdatedAt)],
                    [nameof(WebhookLocalTargetSnapshot.DeliveryStatusId), nameof(WebhookLocalTargetSnapshot.ProcessingLeaseOwner),
                        nameof(WebhookLocalTargetSnapshot.ProcessingLeaseToken), nameof(WebhookLocalTargetSnapshot.ProcessingLeaseExpiresAtUtc),
                        nameof(WebhookLocalTargetSnapshot.DeliveryFence), nameof(WebhookLocalTargetSnapshot.ConcurrencyVersion),
                        nameof(WebhookLocalTargetSnapshot.NextActionAtUtc), nameof(WebhookLocalTargetSnapshot.UpdatedAt)], []),
                Relation<WebhookEndpoint>([nameof(WebhookEndpoint.Id), nameof(WebhookEndpoint.StatusId)], [], []),
                Relation<WebhookDeliveryAttempt>([], [],
                    [nameof(WebhookDeliveryAttempt.Id), nameof(WebhookDeliveryAttempt.TenantId),
                        nameof(WebhookDeliveryAttempt.MessageId), nameof(WebhookDeliveryAttempt.EndpointId),
                        nameof(WebhookDeliveryAttempt.AttemptNumber), nameof(WebhookDeliveryAttempt.OutcomeId),
                        nameof(WebhookDeliveryAttempt.ScheduledAt), nameof(WebhookDeliveryAttempt.SentAt),
                        nameof(WebhookDeliveryAttempt.CompletedAt), nameof(WebhookDeliveryAttempt.ProcessingFence),
                        nameof(WebhookDeliveryAttempt.FailureCategory), nameof(WebhookDeliveryAttempt.CreatedAt)]),
                Relation<SystemSetting>(context.Model.FindEntityType(typeof(SystemSetting))!.GetProperties()
                    .Select(property => property.Name).ToArray(), [], []),
                Relation<TenantSetting>(context.Model.FindEntityType(typeof(TenantSetting))!.GetProperties()
                    .Select(property => property.Name).ToArray(), [], [])
            ],
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)
            ]);
    }

    private static string EnrollmentSql(PostgresPrivilegeGuard.Contract contract, PostgresNativeQueueEnrollment enrollment) => $"""
        DO $enrollment$
        DECLARE v_role text;
        BEGIN
            IF session_user IS DISTINCT FROM {Text(enrollment.MigratorLogin)}
                OR EXISTS (SELECT 1 FROM unnest(ARRAY[{Text(enrollment.MigratorLogin)}, {Text(enrollment.GlobalLogin)},
                    {Text(enrollment.RuntimeLogin)}, {Text(enrollment.EmailInstanceMutationLogin)},
                    {Text(enrollment.EmailTenantMutationLogin)}]) login(name)
                    LEFT JOIN pg_catalog.pg_roles role ON role.rolname = login.name
                    WHERE role.oid IS NULL OR NOT role.rolcanlogin
                        OR login.name IN ('{OwnerRole}', '{MigratorRole}', '{GlobalRole}')) THEN
                RAISE EXCEPTION 'native_queue_enrollment_not_ready' USING ERRCODE = '42501';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                WHERE rolname IN ('{OwnerRole}', '{MigratorRole}', '{GlobalRole}')) THEN
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc routine
                    JOIN pg_catalog.pg_namespace namespace ON namespace.oid = routine.pronamespace
                    WHERE namespace.nspname = {Text(contract.Schema)}
                        AND routine.proname IN ({string.Join(", ", contract.Routines.Select(routine => Text(routine.Name)))}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy policy
                        JOIN pg_catalog.pg_class relation ON relation.oid = policy.polrelid
                        JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                        WHERE namespace.nspname = {Text(contract.Schema)} AND policy.polname = '{PolicyName}') THEN
                    RAISE EXCEPTION 'native_privilege_guard_not_ready' USING ERRCODE = '55000';
                END IF;
                FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{GlobalRole}'] LOOP
                    EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                END LOOP;
                GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
                EXECUTE format('GRANT %I TO %I WITH INHERIT TRUE, SET FALSE, ADMIN FALSE', '{GlobalRole}', {Text(enrollment.GlobalLogin)});
                EXECUTE format('GRANT %I TO %I WITH INHERIT FALSE, SET TRUE, ADMIN FALSE', '{MigratorRole}', {Text(enrollment.MigratorLogin)});
                FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{GlobalRole}'] LOOP
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members membership
                        JOIN pg_catalog.pg_roles parent ON parent.oid = membership.roleid
                        JOIN pg_catalog.pg_roles member ON member.oid = membership.member
                        WHERE parent.rolname = v_role AND member.rolname = {Text(enrollment.MigratorLogin)}) THEN
                        EXECUTE format('REVOKE %I FROM %I', v_role, {Text(enrollment.MigratorLogin)});
                    END IF;
                END LOOP;
                IF ({PostgresPrivilegeGuard.BuildRoleGraphSql(contract)}) IS NOT TRUE THEN
                    RAISE EXCEPTION 'native_privilege_guard_not_ready' USING ERRCODE = '55000';
                END IF;
            ELSE
                {PostgresPrivilegeGuard.BuildAssertSql(contract)}
            END IF;
        END $enrollment$;
        """;

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
