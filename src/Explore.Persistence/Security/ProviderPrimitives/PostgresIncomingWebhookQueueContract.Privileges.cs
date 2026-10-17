using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Security;

public static partial class PostgresIncomingWebhookQueueContract
{
    private static PostgresPrivilegeGuard.Contract PrivilegeContract(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        PostgresPrivilegeGuard.Relation Relation(Type type, string[] reads, string[] writes, string[] inserts)
        {
            var entity = context.Model.FindEntityType(type)!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return new(entity.GetTableName()!, OwnerRole, [],
                reads.Concat(writes).Concat(inserts).Distinct(StringComparer.Ordinal).Select(property =>
                    new PostgresPrivilegeGuard.Column(entity.FindProperty(property)!.GetColumnName(store)!,
                        (reads.Contains(property) ? new[] { "SELECT" } : [])
                        .Concat(writes.Contains(property) ? ["UPDATE"] : [])
                        .Concat(inserts.Contains(property) ? ["INSERT"] : []).ToArray())).ToArray());
        }
        PostgresPrivilegeGuard.Relation Queue(bool effects) => Relation(
            effects ? typeof(IncomingWebhookEffectOutbox) : typeof(IncomingWebhookMessage),
            [nameof(IncomingWebhookMessage.Id), nameof(IncomingWebhookMessage.TenantId),
                effects ? nameof(IncomingWebhookEffectOutbox.Status) : nameof(IncomingWebhookMessage.StatusId),
                nameof(IncomingWebhookMessage.NextAttemptAt), nameof(IncomingWebhookMessage.ProcessingLeaseExpiresAt),
                nameof(IncomingWebhookMessage.ProcessingLeaseToken), nameof(IncomingWebhookMessage.ProcessingFence),
                nameof(IncomingWebhookMessage.ProcessingGeneration), nameof(IncomingWebhookMessage.AttemptCount),
                nameof(IncomingWebhookMessage.ProcessingStartedAt),
                effects ? nameof(IncomingWebhookEffectOutbox.CreatedAt) : nameof(IncomingWebhookMessage.ReceivedAt)],
            [effects ? nameof(IncomingWebhookEffectOutbox.Status) : nameof(IncomingWebhookMessage.StatusId),
                nameof(IncomingWebhookMessage.NextAttemptAt), nameof(IncomingWebhookMessage.ProcessingLeaseExpiresAt),
                nameof(IncomingWebhookMessage.ProcessingLeaseToken), nameof(IncomingWebhookMessage.ProcessingLeaseOwner),
                nameof(IncomingWebhookMessage.ProcessingFence), nameof(IncomingWebhookMessage.AttemptCount),
                nameof(IncomingWebhookMessage.ProcessingStartedAt), nameof(IncomingWebhookMessage.UpdatedAt),
                nameof(IncomingWebhookMessage.FailureCategory), nameof(IncomingWebhookMessage.SafeDetail)], []);
        return new(Schema(context), [OwnerRole, MigratorRole, GlobalRole],
            [
                new("incoming_webhook_message_claim", "text, integer, timestamp with time zone, interval", OwnerRole, [GlobalRole]),
                new("incoming_webhook_effect_claim", "text, integer, timestamp with time zone, interval", OwnerRole, [GlobalRole]),
                new("incoming_webhook_message_renew", "uuid, uuid, uuid, bigint, integer, timestamp with time zone, timestamp with time zone", OwnerRole, [GlobalRole]),
                new("incoming_webhook_effect_renew", "uuid, uuid, uuid, bigint, integer, timestamp with time zone, timestamp with time zone", OwnerRole, [GlobalRole])
            ],
            [
                Queue(false), Queue(true),
                Relation(typeof(IncomingWebhookProcessingAttempt), [], [],
                    [nameof(IncomingWebhookProcessingAttempt.Id), nameof(IncomingWebhookProcessingAttempt.TenantId),
                        nameof(IncomingWebhookProcessingAttempt.IncomingWebhookMessageId),
                        nameof(IncomingWebhookProcessingAttempt.ProcessingGeneration),
                        nameof(IncomingWebhookProcessingAttempt.ProcessingFence),
                        nameof(IncomingWebhookProcessingAttempt.AttemptNumber), nameof(IncomingWebhookProcessingAttempt.OutcomeId),
                        nameof(IncomingWebhookProcessingAttempt.StartedAt), nameof(IncomingWebhookProcessingAttempt.RecordedAt),
                        nameof(IncomingWebhookProcessingAttempt.FailureCategory), nameof(IncomingWebhookProcessingAttempt.SafeDetail),
                        nameof(IncomingWebhookProcessingAttempt.CreatedAt)])
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
