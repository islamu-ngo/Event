using Explore.Domain;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

public static partial class PostgresEmailDeliveryPolicyContract
{
    private static string BuildProcessorMutationSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var entity = context.Model.FindEntityType(typeof(EmailDispatchProcessorState))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string table = sql.DelimitIdentifier(entity.GetTableName()!, Schema(context));
        string function = ControlsFunction(context, "_processor_guard");
        string C(string property) => sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(store)!);
        string[] operatorProperties =
        [
            nameof(EmailDispatchProcessorState.IsPaused), nameof(EmailDispatchProcessorState.PauseReason),
            nameof(EmailDispatchProcessorState.PausedAt), nameof(EmailDispatchProcessorState.PausedBy),
            nameof(EmailDispatchProcessorState.GlobalSmtpRateLimitPerMinuteOverride),
            nameof(EmailDispatchProcessorState.OptionalRemindersDeferred),
            nameof(EmailDispatchProcessorState.SmtpAvailableTokens), nameof(EmailDispatchProcessorState.SmtpRefillAt)
        ];
        string protectedNew = string.Join(", ", operatorProperties.Select(property => $"NEW.{C(property)}"));
        string protectedOld = string.Join(", ", operatorProperties.Select(property => $"OLD.{C(property)}"));
        string unsafeInitial = string.Join(" OR ", operatorProperties.Select(property =>
            property is nameof(EmailDispatchProcessorState.IsPaused) or nameof(EmailDispatchProcessorState.OptionalRemindersDeferred)
                ? $"NEW.{C(property)} IS DISTINCT FROM false"
                : $"NEW.{C(property)} IS NOT NULL"));
        return $"""
            GRANT CREATE ON SCHEMA {sql.DelimitIdentifier(Schema(context))} TO {OwnerRole};
            DO $processor$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}()
                RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                BEGIN
                    {PrivilegeAssertion(context, enrollment)}
                    IF session_user IS DISTINCT FROM {LoginLiteral(enrollment.EmailInstanceMutationLogin)} THEN
                        RETURN NEW;
                    END IF;
                    {PostgresPrivilegeGuard.RestrictedCallerSql}
                    PERFORM {ControlsFunction(context, "_require_mutation")}(NULL::uuid);
                    IF NEW.{C(nameof(EmailDispatchProcessorState.ProcessorCode))} IS DISTINCT FROM
                        {LoginLiteral(EmailDispatchOutboxRepository.SmtpProcessorCode)} THEN
                        RAISE EXCEPTION 'email_processor_scope_invalid' USING ERRCODE = '42501';
                    END IF;
                    IF TG_OP = 'INSERT' AND ({unsafeInitial}) THEN
                        RAISE EXCEPTION 'email_processor_initial_operator_state_invalid' USING ERRCODE = '42501';
                    END IF;
                    IF TG_OP = 'UPDATE' AND (
                        ROW({protectedNew}) IS DISTINCT FROM ROW({protectedOld})
                        OR NEW.{C(nameof(EmailDispatchProcessorState.Id))} IS DISTINCT FROM OLD.{C(nameof(EmailDispatchProcessorState.Id))}
                        OR NEW.{C(nameof(EmailDispatchProcessorState.ProcessorCode))} IS DISTINCT FROM OLD.{C(nameof(EmailDispatchProcessorState.ProcessorCode))}) THEN
                        RAISE EXCEPTION 'email_processor_operator_state_protected' USING ERRCODE = '42501';
                    END IF;
                    RETURN NEW;
                END $body$ $ddl$;
                EXECUTE 'REVOKE ALL ON FUNCTION {function}() FROM PUBLIC, {RuntimeRole}, {GlobalRole},
                    {InstanceMutationRole}, {TenantMutationRole}';
                PERFORM set_config('role', v_previous_role, true);
            END $processor$;
            DROP TRIGGER IF EXISTS email_policy_processor_mutation ON {table};
            CREATE TRIGGER email_policy_processor_mutation BEFORE INSERT OR UPDATE ON {table}
                FOR EACH ROW EXECUTE FUNCTION {function}();
            REVOKE CREATE ON SCHEMA {sql.DelimitIdentifier(Schema(context))} FROM {OwnerRole};
            """;
    }
}
