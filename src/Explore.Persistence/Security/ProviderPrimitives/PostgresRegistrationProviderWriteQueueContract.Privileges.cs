using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Security;

public static partial class PostgresRegistrationProviderWriteQueueContract
{
    private static PostgresPrivilegeGuard.Contract PrivilegeContract(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        PostgresPrivilegeGuard.Relation Relation<T>(string[] reads, string[] writes)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return new(entity.GetTableName()!, OwnerRole, [],
                reads.Concat(writes).Distinct(StringComparer.Ordinal).Select(property =>
                    new PostgresPrivilegeGuard.Column(entity.FindProperty(property)!.GetColumnName(store)!,
                        (reads.Contains(property) ? new[] { "SELECT" } : [])
                        .Concat(writes.Contains(property) ? ["UPDATE"] : []).ToArray())).ToArray());
        }
        return new(Schema(context), [OwnerRole, MigratorRole, GlobalRole],
            [new("registration_provider_write_claim", "text, integer, timestamp with time zone, interval", OwnerRole, [GlobalRole])],
            [
                Relation<RegistrationProviderSubmissionWriteEffect>(
                    [nameof(RegistrationProviderSubmissionWriteEffect.Id), nameof(RegistrationProviderSubmissionWriteEffect.TenantId),
                        nameof(RegistrationProviderSubmissionWriteEffect.RegistrationSubmissionId),
                        nameof(RegistrationProviderSubmissionWriteEffect.RegistrationAttemptId),
                        nameof(RegistrationProviderSubmissionWriteEffect.RegistrationProviderBindingId),
                        nameof(RegistrationProviderSubmissionWriteEffect.Status), nameof(RegistrationProviderSubmissionWriteEffect.NextAttemptAt),
                        nameof(RegistrationProviderSubmissionWriteEffect.ProcessingLeaseExpiresAt),
                        nameof(RegistrationProviderSubmissionWriteEffect.ProcessingFence),
                        nameof(RegistrationProviderSubmissionWriteEffect.AttemptCount), nameof(RegistrationProviderSubmissionWriteEffect.CreatedAt)],
                    [nameof(RegistrationProviderSubmissionWriteEffect.Status), nameof(RegistrationProviderSubmissionWriteEffect.NextAttemptAt),
                        nameof(RegistrationProviderSubmissionWriteEffect.ProcessingLeaseExpiresAt),
                        nameof(RegistrationProviderSubmissionWriteEffect.ProcessingLeaseToken),
                        nameof(RegistrationProviderSubmissionWriteEffect.ProcessingLeaseOwner),
                        nameof(RegistrationProviderSubmissionWriteEffect.ProcessingFence),
                        nameof(RegistrationProviderSubmissionWriteEffect.AttemptCount), nameof(RegistrationProviderSubmissionWriteEffect.UpdatedAt)]),
                Relation<StorageObject>([nameof(StorageObject.Id), nameof(StorageObject.TenantId),
                    nameof(StorageObject.OwningResourceId), nameof(StorageObject.OwningResourceKind)],
                    [nameof(StorageObject.ConcurrencyStamp)])
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
                        WHERE namespace.nspname = {Text(contract.Schema)}
                            AND policy.polname IN ('registration_provider_write_queue', 'registration_provider_write_storage_fence')) THEN
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
