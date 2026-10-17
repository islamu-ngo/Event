using Explore.Domain;
using Explore.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresOutboxExecutionContract
{
    private static readonly string[] Columns =
    [
        "id", "aggregate_id", "aggregate_type", "event_type", "payload", "status", "created_at",
        "processed_at", "retry_count", "last_error", "next_retry_at", "max_retries", "dead_lettered_at"
    ];
    private static readonly string[] MutableColumns =
        ["payload", "status", "next_retry_at", "last_error", "retry_count", "processed_at", "dead_lettered_at"];
    private static readonly string[] PublicationProperties =
        ["Id", "TenantId", "ActorId", "Title", "EventStatusId", "FirstSessionStartUtc", "LastSessionStartUtc", "IsDeleted"];
    private const string PublicationPolicy = "outbox_execution_publication_source";

    private static PostgresPrivilegeGuard.Contract Declaration(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string table = context.Model.FindEntityType(typeof(OutboxMessage))!.GetTableName()!;
        return new(sql.Schema, [OwnerRole, MigratorRole, RuntimeRole],
            [new(Routine, Signature, OwnerRole, [RuntimeRole])],
            [
                new(table, OwnerRole, [], Columns.Select(column => new PostgresPrivilegeGuard.Column(
                    column, MutableColumns.Contains(column, StringComparer.Ordinal) ? ["SELECT", "UPDATE"] : ["SELECT"])).ToArray()),
                new(context.Model.FindEntityType(typeof(Event))!.GetTableName()!, OwnerRole, [],
                    PublicationProperties.Select(property => new PostgresPrivilegeGuard.Column(
                        context.Model.FindEntityType(typeof(Event))!.FindProperty(property)!.GetColumnName()!, ["SELECT"])).ToArray())
            ],
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false)
            ]);
    }

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var sql = new HumanPrincipalSourceSql(context);
        var declaration = Declaration(context, enrollment);
        string schema = "\"" + sql.Schema.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        string name = sql.Name(Routine);
        string quotedMigrator = "\"" + enrollment.MigratorLogin.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        string quotedRuntime = "\"" + enrollment.RuntimeLogin.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        return $"""
            DO $preflight$ BEGIN
                IF pg_catalog.to_regprocedure({Text(name + "(" + Signature + ")")}) IS NOT NULL THEN
                    {Guard(context, enrollment)}
                    {ExactBody(context, enrollment)}
                END IF;
                IF pg_catalog.to_regrole('{OwnerRole}') IS NOT NULL
                    OR pg_catalog.to_regrole('{MigratorRole}') IS NOT NULL
                    OR pg_catalog.to_regrole('{RuntimeRole}') IS NOT NULL THEN
                    IF NOT ({PostgresPrivilegeGuard.BuildRoleGraphSql(declaration)})
                    THEN RAISE EXCEPTION 'outbox_execution_role_drift' USING ERRCODE = '55000'; END IF;
                END IF;
            END $preflight$;
            DO $roles$ DECLARE role_name text; BEGIN
                FOREACH role_name IN ARRAY ARRAY['{OwnerRole}','{MigratorRole}','{RuntimeRole}'] LOOP
                    IF pg_catalog.to_regrole(role_name) IS NULL THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS',role_name);
                    END IF;
                END LOOP;
            END $roles$;
            GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
            GRANT {MigratorRole} TO {quotedMigrator} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
            GRANT {RuntimeRole} TO {quotedRuntime} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE;
            GRANT USAGE,CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {RuntimeRole};
            GRANT SELECT ({string.Join(",", Columns)}) ON {sql.Table(typeof(OutboxMessage))} TO {OwnerRole};
            GRANT UPDATE ({string.Join(",", MutableColumns)}) ON {sql.Table(typeof(OutboxMessage))} TO {OwnerRole};
            GRANT SELECT ({string.Join(",", PublicationProperties.Select(property => sql.Column(typeof(Event), property)))})
                ON {sql.Table(typeof(Event))} TO {OwnerRole};
            DROP POLICY IF EXISTS {PublicationPolicy} ON {sql.Table(typeof(Event))};
            CREATE POLICY {PublicationPolicy} ON {sql.Table(typeof(Event))} FOR SELECT TO {OwnerRole} USING (true);
            GRANT EXECUTE ON FUNCTION {PostgresNotificationOutboxAdmissionContract.ReadFunctionSql(context)}(uuid,timestamp with time zone) TO {OwnerRole};
            GRANT EXECUTE ON FUNCTION {PostgresGenericOutboxAdmissionContract.ReadFunctionSql(context)}(uuid,timestamp with time zone,integer) TO {OwnerRole};
            DO $bootstrap$ DECLARE previous_role text := pg_catalog.current_setting('role'); BEGIN
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {name}(
                    p_claim jsonb,p_notification boolean,p_operation integer,p_status integer,
                    p_error text,p_retryable boolean,p_delay integer,p_failed timestamp with time zone,
                    p_expected text,p_replacement text)
                RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $function${Body(context, enrollment)}$function$ $ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
            END $bootstrap$;
            REVOKE ALL ON FUNCTION {name}({Signature}) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION {name}({Signature}) TO {RuntimeRole};
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $ready$ BEGIN {Guard(context, enrollment)} END $ready$;
            """;
    }

    private static string Body(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string table = sql.Table(typeof(OutboxMessage));
        string projection = "pg_catalog.jsonb_build_object(" + string.Join(",", context.Model
            .FindEntityType(typeof(OutboxMessage))!.GetProperties().Select(property =>
            {
                string value = "r." + sql.Column(typeof(OutboxMessage), property.Name);
                if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTime))
                    value = $"pg_catalog.to_char({value} AT TIME ZONE 'UTC','YYYY-MM-DD\"T\"HH24:MI:SS.US') || 'Z'";
                return Text(property.Name) + "," + value;
            })) + ")";
        return $"""
            DECLARE
                r record; current_claim jsonb; source_image jsonb; expected_claim jsonb := p_claim;
                message_id uuid := (p_claim->>'MessageId')::uuid;
                v_tenant uuid := (p_claim->>'TenantId')::uuid;
                expiry timestamp with time zone;
            BEGIN
                {Guard(context, enrollment)}
                {PostgresPrivilegeGuard.RestrictedCallerSql}
                IF session_user <> {Text(enrollment.RuntimeLogin)} OR current_user <> '{OwnerRole}'
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                    OR pg_catalog.current_setting('session_replication_role') <> 'origin'
                    OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM v_tenant
                    OR pg_catalog.pg_has_role(session_user,{Text(enrollment.MigratorLogin)},'SET')
                    OR pg_catalog.pg_has_role(session_user,{Text(enrollment.MigratorLogin)},'USAGE')
                    OR p_operation IS NULL OR p_operation NOT BETWEEN 0 AND 5
                    OR p_status IS NULL OR p_status NOT IN ({(int)OutboxMessageStatus.Processing},{(int)OutboxMessageStatus.DeadLettered})
                    OR p_notification IS NULL OR p_notification AND p_status <> {(int)OutboxMessageStatus.Processing}
                    OR p_operation IN (1,2,4,5) AND p_status <> {(int)OutboxMessageStatus.Processing}
                    OR p_operation = 3 AND p_status <> {(int)OutboxMessageStatus.DeadLettered}
                THEN RAISE EXCEPTION 'outbox_execution_original_service' USING ERRCODE = '42501'; END IF;
                expiry := (p_claim->>CASE WHEN p_notification THEN 'ProcessingLeaseExpiresAt' ELSE 'LeaseExpiresAt' END)::timestamptz;
                IF expiry IS NULL OR NOT pg_catalog.isfinite(expiry) OR expiry <= pg_catalog.clock_timestamp()
                    THEN RETURN NULL; END IF;
                SELECT * INTO r FROM {table} WHERE id = message_id AND status = p_status
                    AND next_retry_at = expiry FOR UPDATE;
                IF NOT FOUND THEN RETURN NULL; END IF;
                IF p_notification THEN
                    expected_claim := pg_catalog.jsonb_set(expected_claim,ARRAY['ProcessingLeaseExpiresAt'],pg_catalog.to_jsonb(expiry));
                    SELECT pg_catalog.to_jsonb(c) INTO current_claim
                        FROM {PostgresNotificationOutboxAdmissionContract.ReadFunctionSql(context)}(message_id,expiry) c;
                ELSE
                    expected_claim := pg_catalog.jsonb_set(expected_claim,ARRAY['LeaseExpiresAt'],pg_catalog.to_jsonb(expiry));
                    expected_claim := pg_catalog.jsonb_set(expected_claim,ARRAY['CreatedAt'],pg_catalog.to_jsonb((p_claim->>'CreatedAt')::timestamptz));
                    expected_claim := pg_catalog.jsonb_set(expected_claim,ARRAY['Status'],pg_catalog.to_jsonb(p_status));
                    SELECT pg_catalog.to_jsonb(c) INTO current_claim
                        FROM {PostgresGenericOutboxAdmissionContract.ReadFunctionSql(context)}(message_id,expiry,p_status) c;
                END IF;
                IF current_claim IS NULL OR current_claim IS DISTINCT FROM expected_claim
                    OR expiry <= pg_catalog.clock_timestamp() THEN RETURN NULL; END IF;
                CASE p_operation
                    WHEN 0 THEN RETURN {projection};
                    WHEN 1 THEN
                        UPDATE {table} SET status = {(int)OutboxMessageStatus.Completed},
                            processed_at = pg_catalog.clock_timestamp(),next_retry_at = NULL,last_error = NULL WHERE id = message_id;
                    WHEN 2 THEN
                        IF p_failed IS NULL OR NOT pg_catalog.isfinite(p_failed) OR p_retryable IS NULL
                            OR p_delay IS NULL OR p_delay < 0 OR p_error IS NULL
                            THEN RAISE EXCEPTION 'outbox_execution_failure_bounds' USING ERRCODE = '22023'; END IF;
                        IF r.retry_count + 1 >= r.max_retries THEN
                            UPDATE {table} SET status = {(int)OutboxMessageStatus.DeadLettered},
                                retry_count = retry_count + 1,last_error = left(p_error,2000),
                                dead_lettered_at = p_failed WHERE id = message_id;
                            RETURN pg_catalog.to_jsonb(3);
                        ELSIF NOT p_retryable THEN
                            UPDATE {table} SET status = {(int)OutboxMessageStatus.Failed},
                                retry_count = retry_count + 1,last_error = left(p_error,2000),
                                next_retry_at = NULL,dead_lettered_at = NULL WHERE id = message_id;
                            RETURN pg_catalog.to_jsonb(2);
                        ELSE
                            UPDATE {table} SET status = {(int)OutboxMessageStatus.Pending},
                                retry_count = retry_count + 1,last_error = left(p_error,2000),
                                next_retry_at = p_failed + p_delay * interval '1 second',dead_lettered_at = NULL WHERE id = message_id;
                            RETURN pg_catalog.to_jsonb(1);
                        END IF;
                    WHEN 3 THEN UPDATE {table} SET next_retry_at = NULL WHERE id = message_id;
                    WHEN 4 THEN
                        IF p_expected IS NULL OR p_replacement IS NULL OR r.payload IS DISTINCT FROM p_expected
                            THEN RETURN NULL; END IF;
                        UPDATE {table} SET payload = p_replacement WHERE id = message_id;
                    WHEN 5 THEN
                        IF NOT p_notification OR p_claim->>'AggregateType' <> 'Event'
                            OR p_claim->>'EventType' <> {Text(EventPublishedOutboxMessageFactory.EventPublishedNotificationFanoutRequestedEventType)}
                            OR p_expected::uuid IS DISTINCT FROM (p_claim->>'AggregateId')::uuid
                            OR p_expected::uuid IS DISTINCT FROM (p_claim->>'SourceId')::uuid
                        THEN RAISE EXCEPTION 'outbox_execution_publication_source' USING ERRCODE = '42501'; END IF;
                        SELECT pg_catalog.jsonb_build_object(
                            {string.Join(",", PublicationProperties.Select(property => Text(property) + ",e." + sql.Column(typeof(Event), property)))})
                        INTO source_image FROM {sql.Table(typeof(Event))} e
                        WHERE e.{sql.Column(typeof(Event), "Id")} = p_expected::uuid
                            AND e.{sql.Column(typeof(Event), "TenantId")} = v_tenant
                            AND NOT e.{sql.Column(typeof(Event), "IsDeleted")};
                        IF expiry <= pg_catalog.clock_timestamp() THEN RETURN NULL; END IF;
                        RETURN source_image;
                END CASE;
                RETURN pg_catalog.to_jsonb(1);
            END;
            """;
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string notification = PostgresNotificationOutboxAdmissionContract.ReadFunctionSql(context)
            + "(uuid,timestamp with time zone)";
        string generic = PostgresGenericOutboxAdmissionContract.ReadFunctionSql(context)
            + "(uuid,timestamp with time zone,integer)";
        string own = sql.Name(Routine) + "(" + Signature + ")";
        return PostgresPrivilegeGuard.BuildAssertSql(Declaration(context, enrollment))
            + PostgresPrivilegeGuard.BuildRoutineAclAssertSql([notification],
                PostgresNotificationOutboxAdmissionContract.OwnerRole,
                PostgresNotificationOutboxAdmissionContract.GlobalRole, true,
                new Dictionary<string, IReadOnlyList<string>>
                {
                    [notification] = [OwnerRole, PostgresPublicationFanoutExecutionContract.OwnerRole]
                })
            + PostgresPrivilegeGuard.BuildRoutineAclAssertSql([generic],
                PostgresGenericOutboxAdmissionContract.OwnerRole,
                PostgresGenericOutboxAdmissionContract.GlobalRole, true,
                new Dictionary<string, IReadOnlyList<string>> { [generic] = [OwnerRole] })
            + $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    JOIN pg_catalog.pg_class c ON c.oid = p.polrelid
                    WHERE p.polrelid = pg_catalog.to_regclass({Text(sql.Table(typeof(Event)))})
                        AND c.relrowsecurity AND c.relforcerowsecurity
                        AND p.polname = '{PublicationPolicy}' AND p.polcmd = 'r' AND p.polpermissive
                        AND p.polroles = ARRAY['{OwnerRole}'::regrole::oid]
                        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true' AND p.polwithcheck IS NULL)
                THEN RAISE EXCEPTION 'outbox_execution_publication_policy_drift' USING ERRCODE = '55000'; END IF;
                IF NOT pg_catalog.has_function_privilege('{OwnerRole}',{Text(notification)},'EXECUTE')
                    OR NOT pg_catalog.has_function_privilege('{OwnerRole}',{Text(generic)},'EXECUTE')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                        JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                        CROSS JOIN LATERAL pg_catalog.aclexplode(COALESCE(f.proacl,pg_catalog.acldefault('f',f.proowner))) a
                        WHERE n.nspname = {Text(sql.Schema)}
                            AND a.grantee IN ('{OwnerRole}'::regrole,'{MigratorRole}'::regrole,'{RuntimeRole}'::regrole)
                            AND NOT (f.oid = pg_catalog.to_regprocedure({Text(own)})
                                    AND a.grantee IN ('{OwnerRole}'::regrole,'{RuntimeRole}'::regrole)
                                OR f.oid IN (pg_catalog.to_regprocedure({Text(notification)}),pg_catalog.to_regprocedure({Text(generic)}))
                                    AND a.grantee = '{OwnerRole}'::regrole))
                THEN RAISE EXCEPTION 'outbox_execution_nested_authority_drift' USING ERRCODE = '55000'; END IF;
                """;
    }
}
