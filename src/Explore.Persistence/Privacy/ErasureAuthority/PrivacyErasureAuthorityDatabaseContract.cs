namespace Explore.Persistence.Privacy.ErasureAuthority;

public static class PrivacyErasureAuthorityDatabaseContract
{
    public const string SchemaName = "privacy_erasure_authority";
    public const string CounterTable = "authority_counter";
    public const string RollbackGuardView = "retained_evidence_rollback_guard";
    public const string OwnerRole = "privacy_erasure_authority_owner";
    public const string MigratorRole = "privacy_erasure_authority_migrator";
    public const string RuntimeRole = "privacy_erasure_authority_runtime";
    public const string LegacyAppendFunction = "append_erasure_intent";
    public const string AppendFunction = "append_erasure_intent_with_retention";
    public const string ReadFunction = "read_erasure_intents_after";
    public const string GetStateFunction = "get_authority_state";
    public const string EvaluateRetentionFunction = "evaluate_retention";
    public const string CompactRetentionFunction = "compact_expired_intents";
    public const string StaleCheckpointSqlState = "P1001";
    public const string SequenceGapSqlState = "P1002";

    public static string AppendFunctionSql =>
        $"{SchemaName}.{AppendFunction}";

    public static string ReadFunctionSql =>
        $"{SchemaName}.{ReadFunction}";

    public static string GetStateFunctionSql =>
        $"{SchemaName}.{GetStateFunction}";

    public static string EvaluateRetentionFunctionSql =>
        $"{SchemaName}.{EvaluateRetentionFunction}";

    public static string CompactRetentionFunctionSql =>
        $"{SchemaName}.{CompactRetentionFunction}";

    public static string RoleProvisioningSql { get; } = $"""
        DO $contract$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{OwnerRole}') THEN
                CREATE ROLE {OwnerRole} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS;
            ELSE
                ALTER ROLE {OwnerRole} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS;
            END IF;

            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{MigratorRole}') THEN
                CREATE ROLE {MigratorRole} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS;
            ELSE
                ALTER ROLE {MigratorRole} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS;
            END IF;

            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{RuntimeRole}') THEN
                CREATE ROLE {RuntimeRole} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS;
            ELSE
                ALTER ROLE {RuntimeRole} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS;
            END IF;
        END
        $contract$;

        GRANT {OwnerRole} TO {MigratorRole};
        DO $contract$
        BEGIN
            IF EXISTS (
                WITH RECURSIVE inherited_roles(roleid) AS
                (
                    SELECT edge.roleid
                    FROM pg_catalog.pg_auth_members AS edge
                    WHERE edge.member = (
                        SELECT role.oid
                        FROM pg_catalog.pg_roles AS role
                        WHERE role.rolname = CURRENT_USER)
                    UNION
                    SELECT edge.roleid
                    FROM pg_catalog.pg_auth_members AS edge
                    INNER JOIN inherited_roles AS inherited
                        ON edge.member = inherited.roleid
                )
                SELECT 1
                FROM inherited_roles
                WHERE roleid = (
                    SELECT role.oid
                    FROM pg_catalog.pg_roles AS role
                    WHERE role.rolname = '{RuntimeRole}')) THEN
                RAISE EXCEPTION 'privacy erasure authority runtime and migrator logins must remain separate';
            END IF;
        END
        $contract$;
        GRANT {MigratorRole} TO CURRENT_USER;
        """;

    public static string AuthorityObjectsSql { get; } = $"""
        CREATE SCHEMA IF NOT EXISTS {SchemaName};
        ALTER SCHEMA {SchemaName} OWNER TO {OwnerRole};

        ALTER TABLE {SchemaName}.erasure_intents OWNER TO {OwnerRole};
        ALTER TABLE {SchemaName}.authority_counter OWNER TO {OwnerRole};
        ALTER TABLE {SchemaName}.erasure_intents
            ALTER COLUMN requested_at_utc SET DEFAULT statement_timestamp(),
            ALTER COLUMN recorded_at_utc SET DEFAULT clock_timestamp(),
            ALTER COLUMN retention_expires_at_utc
                SET DEFAULT 'infinity'::timestamp with time zone;

        INSERT INTO {SchemaName}.authority_counter (singleton, last_sequence)
        SELECT true, COALESCE(MAX(authority_sequence), 0)
        FROM {SchemaName}.erasure_intents
        ON CONFLICT (singleton) DO UPDATE
        SET last_sequence = GREATEST(
            {SchemaName}.authority_counter.last_sequence,
            EXCLUDED.last_sequence);

        CREATE OR REPLACE FUNCTION {SchemaName}.reject_erasure_intent_mutation()
        RETURNS trigger
        LANGUAGE plpgsql
        SET search_path = pg_catalog, {SchemaName}
        AS $function$
        BEGIN
            RAISE EXCEPTION 'privacy erasure authority facts are immutable'
                USING ERRCODE = '55000';
        END;
        $function$;
        ALTER FUNCTION {SchemaName}.reject_erasure_intent_mutation()
            OWNER TO {OwnerRole};

        DROP TRIGGER IF EXISTS tr_erasure_intents_immutable
            ON {SchemaName}.erasure_intents;
        CREATE TRIGGER tr_erasure_intents_immutable
        BEFORE UPDATE OR DELETE ON {SchemaName}.erasure_intents
        FOR EACH ROW
        EXECUTE FUNCTION {SchemaName}.reject_erasure_intent_mutation();

        DROP TRIGGER IF EXISTS tr_erasure_intents_no_truncate
            ON {SchemaName}.erasure_intents;
        CREATE TRIGGER tr_erasure_intents_no_truncate
        BEFORE TRUNCATE ON {SchemaName}.erasure_intents
        FOR EACH STATEMENT
        EXECUTE FUNCTION {SchemaName}.reject_erasure_intent_mutation();

        CREATE OR REPLACE FUNCTION {SchemaName}.{LegacyAppendFunction}(
            p_intent_id uuid,
            p_subject_kind smallint,
            p_subject_id uuid,
            p_reason_code smallint,
            p_policy_version integer)
        RETURNS TABLE
        (
            authority_sequence bigint,
            intent_id uuid,
            subject_kind smallint,
            subject_id uuid,
            reason_code smallint,
            policy_version integer,
            requested_at_utc timestamp with time zone,
            recorded_at_utc timestamp with time zone,
            retention_expires_at_utc timestamp with time zone
        )
        LANGUAGE plpgsql
        SECURITY DEFINER
        SET search_path = pg_catalog, {SchemaName}
        AS $function$
        DECLARE
            v_existing {SchemaName}.erasure_intents%ROWTYPE;
            v_last_sequence bigint;
            v_next_sequence bigint;
        BEGIN
            IF p_intent_id IS NULL
               OR p_intent_id = '00000000-0000-0000-0000-000000000000'::uuid
               OR substring(p_intent_id::text from 15 for 1) <> '7'
               OR substring(p_intent_id::text from 20 for 1) NOT IN ('8', '9', 'a', 'b') THEN
                RAISE EXCEPTION 'IntentId must be an RFC 4122 UUIDv7 value'
                    USING ERRCODE = '22023';
            END IF;

            IF p_subject_kind IS NULL OR p_subject_kind <> 1 THEN
                RAISE EXCEPTION 'Only User privacy erasure is executable'
                    USING ERRCODE = '22023';
            END IF;

            IF p_subject_id IS NULL
               OR p_subject_id = '00000000-0000-0000-0000-000000000000'::uuid THEN
                RAISE EXCEPTION 'SubjectId must be an opaque non-empty identifier'
                    USING ERRCODE = '22023';
            END IF;

            IF p_reason_code IS NULL OR p_reason_code NOT BETWEEN 1 AND 3 THEN
                RAISE EXCEPTION 'ReasonCode must be a defined erasure reason'
                    USING ERRCODE = '22023';
            END IF;

            IF p_policy_version IS NULL OR p_policy_version <= 0 THEN
                RAISE EXCEPTION 'PolicyVersion must be positive'
                    USING ERRCODE = '22023';
            END IF;

            SELECT counter.last_sequence
            INTO v_last_sequence
            FROM {SchemaName}.authority_counter AS counter
            WHERE counter.singleton
            FOR UPDATE;

            IF NOT FOUND THEN
                RAISE EXCEPTION 'Erasure authority counter is unavailable'
                    USING ERRCODE = '55000';
            END IF;

            SELECT retained.*
            INTO v_existing
            FROM {SchemaName}.erasure_intents AS retained
            WHERE retained.intent_id = p_intent_id;

            IF FOUND THEN
                IF v_existing.subject_kind <> p_subject_kind
                   OR v_existing.subject_id <> p_subject_id
                   OR v_existing.reason_code <> p_reason_code
                   OR v_existing.policy_version <> p_policy_version THEN
                    RAISE EXCEPTION 'IntentId is already retained with a different payload'
                        USING ERRCODE = '22023';
                END IF;

                RETURN QUERY
                SELECT retained.authority_sequence,
                       retained.intent_id,
                       retained.subject_kind,
                       retained.subject_id,
                       retained.reason_code,
                       retained.policy_version,
                       retained.requested_at_utc,
                       retained.recorded_at_utc,
                       retained.retention_expires_at_utc
                FROM {SchemaName}.erasure_intents AS retained
                WHERE retained.intent_id = p_intent_id;
                RETURN;
            END IF;

            IF v_last_sequence = 9223372036854775807 THEN
                RAISE EXCEPTION 'Erasure authority sequence is exhausted'
                    USING ERRCODE = '22003';
            END IF;

            v_next_sequence := v_last_sequence + 1;
            UPDATE {SchemaName}.authority_counter AS counter
            SET last_sequence = v_next_sequence
            WHERE counter.singleton;

            RETURN QUERY
            INSERT INTO {SchemaName}.erasure_intents AS retained
                (authority_sequence, intent_id, subject_kind, subject_id, reason_code, policy_version)
            VALUES
                (v_next_sequence, p_intent_id, p_subject_kind, p_subject_id, p_reason_code, p_policy_version)
            RETURNING retained.authority_sequence,
                      retained.intent_id,
                      retained.subject_kind,
                      retained.subject_id,
                      retained.reason_code,
                      retained.policy_version,
                      retained.requested_at_utc,
                      retained.recorded_at_utc,
                      retained.retention_expires_at_utc;
        END;
        $function$;
        ALTER FUNCTION {SchemaName}.{LegacyAppendFunction}(uuid, smallint, uuid, smallint, integer)
            OWNER TO {OwnerRole};

        CREATE OR REPLACE FUNCTION {SchemaName}.{ReadFunction}(
            p_authority_sequence bigint,
            p_limit integer)
        RETURNS TABLE
        (
            authority_sequence bigint,
            intent_id uuid,
            subject_kind smallint,
            subject_id uuid,
            reason_code smallint,
            policy_version integer,
            requested_at_utc timestamp with time zone,
            recorded_at_utc timestamp with time zone,
            retention_expires_at_utc timestamp with time zone
        )
        LANGUAGE plpgsql
        STABLE
        SECURITY DEFINER
        SET search_path = pg_catalog, {SchemaName}
        AS $function$
        BEGIN
            IF p_authority_sequence IS NULL OR p_authority_sequence < 0 THEN
                RAISE EXCEPTION 'Authority sequence checkpoint cannot be negative'
                    USING ERRCODE = '22023';
            END IF;

            IF p_limit IS NULL OR p_limit NOT BETWEEN 1 AND 500 THEN
                RAISE EXCEPTION 'Read limit must be between 1 and 500'
                    USING ERRCODE = '22023';
            END IF;

            RETURN QUERY
            SELECT retained.authority_sequence,
                   retained.intent_id,
                   retained.subject_kind,
                   retained.subject_id,
                   retained.reason_code,
                   retained.policy_version,
                   retained.requested_at_utc,
                   retained.recorded_at_utc,
                   retained.retention_expires_at_utc
            FROM {SchemaName}.erasure_intents AS retained
            WHERE retained.authority_sequence > p_authority_sequence
            ORDER BY retained.authority_sequence
            LIMIT p_limit;
        END;
        $function$;
        ALTER FUNCTION {SchemaName}.{ReadFunction}(bigint, integer)
            OWNER TO {OwnerRole};
        """;

    private static string FiniteRetentionAppendFunctionSql { get; } = $"""
        CREATE OR REPLACE FUNCTION {SchemaName}.{AppendFunction}(
            p_intent_id uuid,
            p_subject_kind smallint,
            p_subject_id uuid,
            p_reason_code smallint,
            p_policy_version integer,
            p_authority_retention interval)
        RETURNS TABLE
        (
            authority_sequence bigint,
            intent_id uuid,
            subject_kind smallint,
            subject_id uuid,
            reason_code smallint,
            policy_version integer,
            requested_at_utc timestamp with time zone,
            recorded_at_utc timestamp with time zone,
            retention_expires_at_utc timestamp with time zone
        )
        LANGUAGE plpgsql
        SECURITY DEFINER
        SET search_path = pg_catalog, {SchemaName}
        AS $function$
        DECLARE
            v_existing {SchemaName}.erasure_intents%ROWTYPE;
            v_last_sequence bigint;
            v_next_sequence bigint;
            v_recorded_at_utc timestamp with time zone;
            v_retention_expires_at_utc timestamp with time zone;
        BEGIN
            IF p_intent_id IS NULL
               OR p_intent_id = '00000000-0000-0000-0000-000000000000'::uuid
               OR substring(p_intent_id::text from 15 for 1) <> '7'
               OR substring(p_intent_id::text from 20 for 1) NOT IN ('8', '9', 'a', 'b') THEN
                RAISE EXCEPTION 'IntentId must be an RFC 4122 UUIDv7 value'
                    USING ERRCODE = '22023';
            END IF;

            IF p_subject_kind IS NULL OR p_subject_kind <> 1 THEN
                RAISE EXCEPTION 'Only User privacy erasure is executable'
                    USING ERRCODE = '22023';
            END IF;

            IF p_subject_id IS NULL
               OR p_subject_id = '00000000-0000-0000-0000-000000000000'::uuid THEN
                RAISE EXCEPTION 'SubjectId must be an opaque non-empty identifier'
                    USING ERRCODE = '22023';
            END IF;

            IF p_reason_code IS NULL OR p_reason_code NOT BETWEEN 1 AND 3 THEN
                RAISE EXCEPTION 'ReasonCode must be a defined erasure reason'
                    USING ERRCODE = '22023';
            END IF;

            IF p_policy_version IS NULL OR p_policy_version <= 0 THEN
                RAISE EXCEPTION 'PolicyVersion must be positive'
                    USING ERRCODE = '22023';
            END IF;

            SELECT counter.last_sequence
            INTO v_last_sequence
            FROM {SchemaName}.authority_counter AS counter
            WHERE counter.singleton
            FOR UPDATE;

            IF NOT FOUND THEN
                RAISE EXCEPTION 'Erasure authority counter is unavailable'
                    USING ERRCODE = '55000';
            END IF;

            SELECT retained.*
            INTO v_existing
            FROM {SchemaName}.erasure_intents AS retained
            WHERE retained.intent_id = p_intent_id;

            IF FOUND THEN
                IF v_existing.subject_kind <> p_subject_kind
                   OR v_existing.subject_id <> p_subject_id
                   OR v_existing.reason_code <> p_reason_code
                   OR v_existing.policy_version <> p_policy_version THEN
                    RAISE EXCEPTION 'IntentId is already retained with a different payload'
                        USING ERRCODE = '22023';
                END IF;

                RETURN QUERY
                SELECT retained.authority_sequence,
                       retained.intent_id,
                       retained.subject_kind,
                       retained.subject_id,
                       retained.reason_code,
                       retained.policy_version,
                       retained.requested_at_utc,
                       retained.recorded_at_utc,
                       retained.retention_expires_at_utc
                FROM {SchemaName}.erasure_intents AS retained
                WHERE retained.intent_id = p_intent_id;
                RETURN;
            END IF;

            IF p_authority_retention IS NULL
               OR p_authority_retention <= interval '0' THEN
                RAISE EXCEPTION 'Authority retention must be a positive interval'
                    USING ERRCODE = '22023';
            END IF;

            v_recorded_at_utc := clock_timestamp();
            v_retention_expires_at_utc := v_recorded_at_utc + p_authority_retention;
            IF NOT isfinite(v_retention_expires_at_utc)
               OR v_retention_expires_at_utc <= v_recorded_at_utc THEN
                RAISE EXCEPTION 'Authority retention must produce a finite future expiry'
                    USING ERRCODE = '22023';
            END IF;

            IF v_last_sequence = 9223372036854775807 THEN
                RAISE EXCEPTION 'Erasure authority sequence is exhausted'
                    USING ERRCODE = '22003';
            END IF;

            v_next_sequence := v_last_sequence + 1;
            UPDATE {SchemaName}.authority_counter AS counter
            SET last_sequence = v_next_sequence
            WHERE counter.singleton;

            RETURN QUERY
            INSERT INTO {SchemaName}.erasure_intents AS retained
                (authority_sequence, intent_id, subject_kind, subject_id, reason_code,
                 policy_version, requested_at_utc, recorded_at_utc, retention_expires_at_utc)
            VALUES
                (v_next_sequence, p_intent_id, p_subject_kind, p_subject_id, p_reason_code,
                 p_policy_version, statement_timestamp(), v_recorded_at_utc,
                 v_retention_expires_at_utc)
            RETURNING retained.authority_sequence,
                      retained.intent_id,
                      retained.subject_kind,
                      retained.subject_id,
                      retained.reason_code,
                      retained.policy_version,
                      retained.requested_at_utc,
                      retained.recorded_at_utc,
                      retained.retention_expires_at_utc;
        END;
        $function$;
        ALTER FUNCTION {SchemaName}.{AppendFunction}(uuid, smallint, uuid, smallint, integer, interval)
            OWNER TO {OwnerRole};
        """;

    public static string RetentionLifecycleMigrationSql { get; } = $"""
        DO $contract$
        BEGIN
            IF to_regclass('{SchemaName}.{RollbackGuardView}') IS NULL THEN
                EXECUTE 'CREATE VIEW {SchemaName}.{RollbackGuardView} AS
                    SELECT authority_sequence
                    FROM {SchemaName}.erasure_intents
                    WHERE false';
                EXECUTE 'ALTER VIEW {SchemaName}.{RollbackGuardView}
                    OWNER TO {OwnerRole}';
            END IF;
        END
        $contract$;
        REVOKE ALL ON {SchemaName}.{RollbackGuardView}
            FROM PUBLIC, {RuntimeRole}, {MigratorRole};

        CREATE OR REPLACE FUNCTION {SchemaName}.reject_erasure_intent_mutation()
        RETURNS trigger
        LANGUAGE plpgsql
        SET search_path = pg_catalog, {SchemaName}
        AS $function$
        BEGIN
            IF current_setting('privacy_erasure_authority.maintenance', true) IS DISTINCT FROM 'on' THEN
                RAISE EXCEPTION 'privacy erasure authority facts are immutable'
                    USING ERRCODE = '55000';
            END IF;
            RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
        END;
        $function$;
        ALTER FUNCTION {SchemaName}.reject_erasure_intent_mutation()
            OWNER TO {OwnerRole};

        CREATE OR REPLACE FUNCTION {SchemaName}.{GetStateFunction}()
        RETURNS TABLE
        (
            high_water_sequence bigint,
            retained_floor_sequence bigint
        )
        LANGUAGE plpgsql
        STABLE
        SECURITY DEFINER
        SET search_path = pg_catalog, {SchemaName}
        AS $function$
        BEGIN
            RETURN QUERY
            SELECT counter.last_sequence, counter.retained_floor_sequence
            FROM {SchemaName}.authority_counter AS counter
            WHERE counter.singleton;
            IF NOT FOUND THEN
                RAISE EXCEPTION 'Erasure authority counter is unavailable'
                    USING ERRCODE = '55000';
            END IF;
        END;
        $function$;
        ALTER FUNCTION {SchemaName}.{GetStateFunction}()
            OWNER TO {OwnerRole};

        CREATE OR REPLACE FUNCTION {SchemaName}.{ReadFunction}(
            p_authority_sequence bigint,
            p_limit integer)
        RETURNS TABLE
        (
            authority_sequence bigint,
            intent_id uuid,
            subject_kind smallint,
            subject_id uuid,
            reason_code smallint,
            policy_version integer,
            requested_at_utc timestamp with time zone,
            recorded_at_utc timestamp with time zone,
            retention_expires_at_utc timestamp with time zone
        )
        LANGUAGE plpgsql
        STABLE
        SECURITY DEFINER
        SET search_path = pg_catalog, {SchemaName}
        AS $function$
        DECLARE
            v_retained_floor bigint;
        BEGIN
            IF p_authority_sequence IS NULL OR p_authority_sequence < 0 THEN
                RAISE EXCEPTION 'Authority sequence checkpoint cannot be negative'
                    USING ERRCODE = '22023';
            END IF;
            IF p_limit IS NULL OR p_limit NOT BETWEEN 1 AND 500 THEN
                RAISE EXCEPTION 'Read limit must be between 1 and 500'
                    USING ERRCODE = '22023';
            END IF;

            SELECT counter.retained_floor_sequence
            INTO v_retained_floor
            FROM {SchemaName}.authority_counter AS counter
            WHERE counter.singleton;
            IF NOT FOUND THEN
                RAISE EXCEPTION 'Erasure authority counter is unavailable'
                    USING ERRCODE = '55000';
            END IF;
            IF p_authority_sequence < v_retained_floor THEN
                RAISE EXCEPTION 'Authority checkpoint is below the retained floor'
                    USING ERRCODE = '{StaleCheckpointSqlState}';
            END IF;

            RETURN QUERY
            SELECT retained.authority_sequence,
                   retained.intent_id,
                   retained.subject_kind,
                   retained.subject_id,
                   retained.reason_code,
                   retained.policy_version,
                   retained.requested_at_utc,
                   retained.recorded_at_utc,
                   retained.retention_expires_at_utc
            FROM {SchemaName}.erasure_intents AS retained
            WHERE retained.authority_sequence > p_authority_sequence
            ORDER BY retained.authority_sequence
            LIMIT p_limit;
        END;
        $function$;
        ALTER FUNCTION {SchemaName}.{ReadFunction}(bigint, integer)
            OWNER TO {OwnerRole};

        CREATE OR REPLACE FUNCTION {SchemaName}.{EvaluateRetentionFunction}(
            p_as_of_utc timestamp with time zone,
            p_batch_size integer,
            p_held_authority_sequences bigint[])
        RETURNS TABLE
        (
            eligible_count integer,
            held_count integer,
            current_floor_sequence bigint,
            projected_floor_sequence bigint
        )
        LANGUAGE plpgsql
        STABLE
        SECURITY DEFINER
        SET search_path = pg_catalog, {SchemaName}
        AS $function$
        DECLARE
            v_candidate record;
            v_held bigint[] := p_held_authority_sequences;
            v_expected_sequence bigint;
            v_high_water_sequence bigint;
        BEGIN
            IF p_as_of_utc IS NULL
               OR p_as_of_utc > statement_timestamp()
               OR p_batch_size IS NULL
               OR p_batch_size NOT BETWEEN 1 AND 1000
               OR p_held_authority_sequences IS NULL THEN
                RAISE EXCEPTION 'Retention evaluation parameters are invalid'
                    USING ERRCODE = '22023';
            END IF;
            IF EXISTS (SELECT 1 FROM unnest(v_held) AS held(sequence) WHERE held.sequence <= 0) THEN
                RAISE EXCEPTION 'Held authority sequences must be positive'
                    USING ERRCODE = '22023';
            END IF;

            SELECT counter.last_sequence, counter.retained_floor_sequence
            INTO v_high_water_sequence, current_floor_sequence
            FROM {SchemaName}.authority_counter AS counter
            WHERE counter.singleton;
            IF NOT FOUND THEN
                eligible_count := 0;
                held_count := 0;
                current_floor_sequence := 0;
                projected_floor_sequence := 0;
                RETURN NEXT;
                RETURN;
            END IF;

            eligible_count := 0;
            held_count := 0;
            projected_floor_sequence := current_floor_sequence;
            v_expected_sequence := current_floor_sequence;
            FOR v_candidate IN
                SELECT retained.authority_sequence,
                       retained.retention_expires_at_utc,
                       retained.is_legal_hold_pseudonymized
                FROM {SchemaName}.erasure_intents AS retained
                WHERE retained.authority_sequence > current_floor_sequence
                   OR (retained.authority_sequence = current_floor_sequence
                       AND retained.is_legal_hold_pseudonymized)
                ORDER BY retained.authority_sequence
                LIMIT p_batch_size
            LOOP
                IF v_candidate.authority_sequence = v_expected_sequence
                   AND v_candidate.is_legal_hold_pseudonymized THEN
                    NULL;
                ELSE
                    v_expected_sequence := v_expected_sequence + 1;
                    IF v_candidate.authority_sequence <> v_expected_sequence THEN
                        RAISE EXCEPTION 'Authority sequence gap detected'
                            USING ERRCODE = '{SequenceGapSqlState}';
                    END IF;
                END IF;
                EXIT WHEN v_candidate.retention_expires_at_utc > p_as_of_utc;
                projected_floor_sequence := GREATEST(
                    projected_floor_sequence,
                    v_candidate.authority_sequence);
                IF v_candidate.authority_sequence = ANY(v_held) THEN
                    held_count := held_count + 1;
                    EXIT;
                END IF;
                eligible_count := eligible_count + 1;
            END LOOP;
            IF v_expected_sequence < v_high_water_sequence
               AND NOT EXISTS (
                   SELECT 1
                   FROM {SchemaName}.erasure_intents AS retained
                   WHERE retained.authority_sequence = v_expected_sequence + 1) THEN
                RAISE EXCEPTION 'Authority sequence gap detected'
                    USING ERRCODE = '{SequenceGapSqlState}';
            END IF;
            RETURN NEXT;
        END;
        $function$;
        ALTER FUNCTION {SchemaName}.{EvaluateRetentionFunction}(timestamp with time zone, integer, bigint[])
            OWNER TO {OwnerRole};

        CREATE OR REPLACE FUNCTION {SchemaName}.{CompactRetentionFunction}(
            p_as_of_utc timestamp with time zone,
            p_batch_size integer,
            p_held_authority_sequences bigint[])
        RETURNS TABLE
        (
            deleted_count integer,
            pseudonymized_count integer,
            high_water_sequence bigint,
            retained_floor_sequence bigint
        )
        LANGUAGE plpgsql
        SECURITY DEFINER
        SET search_path = pg_catalog, {SchemaName}
        AS $function$
        DECLARE
            v_candidate record;
            v_changed integer;
            v_held bigint[] := p_held_authority_sequences;
            v_new_floor bigint;
            v_expected_sequence bigint;
        BEGIN
            IF p_as_of_utc IS NULL
               OR p_as_of_utc > statement_timestamp()
               OR p_batch_size IS NULL
               OR p_batch_size NOT BETWEEN 1 AND 1000
               OR p_held_authority_sequences IS NULL THEN
                RAISE EXCEPTION 'Retention compaction parameters are invalid'
                    USING ERRCODE = '22023';
            END IF;
            IF EXISTS (SELECT 1 FROM unnest(v_held) AS held(sequence) WHERE held.sequence <= 0) THEN
                RAISE EXCEPTION 'Held authority sequences must be positive'
                    USING ERRCODE = '22023';
            END IF;

            SELECT counter.last_sequence, counter.retained_floor_sequence
            INTO high_water_sequence, v_new_floor
            FROM {SchemaName}.authority_counter AS counter
            WHERE counter.singleton
            FOR UPDATE;
            IF NOT FOUND THEN
                deleted_count := 0;
                pseudonymized_count := 0;
                high_water_sequence := 0;
                retained_floor_sequence := 0;
                RETURN NEXT;
                RETURN;
            END IF;

            deleted_count := 0;
            pseudonymized_count := 0;
            v_expected_sequence := v_new_floor;
            PERFORM set_config('privacy_erasure_authority.maintenance', 'on', true);
            FOR v_candidate IN
                SELECT retained.authority_sequence,
                       retained.retention_expires_at_utc,
                       retained.is_legal_hold_pseudonymized
                FROM {SchemaName}.erasure_intents AS retained
                WHERE retained.authority_sequence > v_new_floor
                   OR (retained.authority_sequence = v_new_floor
                       AND retained.is_legal_hold_pseudonymized)
                ORDER BY retained.authority_sequence
                LIMIT p_batch_size
            LOOP
                IF v_candidate.authority_sequence = v_expected_sequence
                   AND v_candidate.is_legal_hold_pseudonymized THEN
                    NULL;
                ELSE
                    v_expected_sequence := v_expected_sequence + 1;
                    IF v_candidate.authority_sequence <> v_expected_sequence THEN
                        RAISE EXCEPTION 'Authority sequence gap detected'
                            USING ERRCODE = '{SequenceGapSqlState}';
                    END IF;
                END IF;
                EXIT WHEN v_candidate.retention_expires_at_utc > p_as_of_utc;
                IF v_candidate.authority_sequence = ANY(v_held) THEN
                    IF NOT v_candidate.is_legal_hold_pseudonymized THEN
                        UPDATE {SchemaName}.erasure_intents AS retained
                        SET intent_id = gen_random_uuid(),
                            subject_id = gen_random_uuid(),
                            is_legal_hold_pseudonymized = true
                        WHERE retained.authority_sequence = v_candidate.authority_sequence;
                        GET DIAGNOSTICS v_changed = ROW_COUNT;
                        pseudonymized_count := pseudonymized_count + v_changed;
                    END IF;
                    v_new_floor := GREATEST(
                        v_new_floor,
                        v_candidate.authority_sequence);
                    EXIT;
                END IF;

                DELETE FROM {SchemaName}.erasure_intents AS retained
                WHERE retained.authority_sequence = v_candidate.authority_sequence;
                GET DIAGNOSTICS v_changed = ROW_COUNT;
                deleted_count := deleted_count + v_changed;
                v_new_floor := GREATEST(
                    v_new_floor,
                    v_candidate.authority_sequence);
            END LOOP;

            IF v_expected_sequence < high_water_sequence
               AND NOT EXISTS (
                   SELECT 1
                   FROM {SchemaName}.erasure_intents AS retained
                   WHERE retained.authority_sequence = v_expected_sequence + 1) THEN
                RAISE EXCEPTION 'Authority sequence gap detected'
                    USING ERRCODE = '{SequenceGapSqlState}';
            END IF;

            UPDATE {SchemaName}.authority_counter AS counter
            SET retained_floor_sequence = v_new_floor
            WHERE counter.singleton;
            retained_floor_sequence := v_new_floor;
            RETURN NEXT;
        END;
        $function$;
        ALTER FUNCTION {SchemaName}.{CompactRetentionFunction}(timestamp with time zone, integer, bigint[])
            OWNER TO {OwnerRole};

        REVOKE ALL ON FUNCTION {SchemaName}.{GetStateFunction}() FROM PUBLIC, {RuntimeRole};
        REVOKE ALL ON FUNCTION {SchemaName}.{EvaluateRetentionFunction}(timestamp with time zone, integer, bigint[])
            FROM PUBLIC, {RuntimeRole};
        REVOKE ALL ON FUNCTION {SchemaName}.{CompactRetentionFunction}(timestamp with time zone, integer, bigint[])
            FROM PUBLIC, {RuntimeRole}, {MigratorRole};
        GRANT EXECUTE ON FUNCTION {SchemaName}.{GetStateFunction}() TO {RuntimeRole};
        GRANT EXECUTE ON FUNCTION {SchemaName}.{EvaluateRetentionFunction}(timestamp with time zone, integer, bigint[])
            TO {RuntimeRole};
        GRANT EXECUTE ON FUNCTION {SchemaName}.{CompactRetentionFunction}(timestamp with time zone, integer, bigint[])
            TO {MigratorRole};
        """;

    public static string RuntimeAclSql { get; } = $"""
        REVOKE ALL ON SCHEMA {SchemaName} FROM PUBLIC;
        REVOKE ALL ON SCHEMA {SchemaName} FROM {RuntimeRole};
        REVOKE ALL ON ALL TABLES IN SCHEMA {SchemaName} FROM PUBLIC, {RuntimeRole};
        REVOKE ALL ON ALL SEQUENCES IN SCHEMA {SchemaName} FROM PUBLIC, {RuntimeRole};
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA {SchemaName} FROM PUBLIC, {RuntimeRole};
        ALTER DEFAULT PRIVILEGES FOR ROLE {OwnerRole} IN SCHEMA {SchemaName}
            REVOKE ALL ON TABLES FROM PUBLIC, {RuntimeRole};
        ALTER DEFAULT PRIVILEGES FOR ROLE {OwnerRole} IN SCHEMA {SchemaName}
            REVOKE ALL ON SEQUENCES FROM PUBLIC, {RuntimeRole};
        ALTER DEFAULT PRIVILEGES FOR ROLE {OwnerRole} IN SCHEMA {SchemaName}
            REVOKE ALL ON FUNCTIONS FROM PUBLIC, {RuntimeRole};
        GRANT USAGE ON SCHEMA {SchemaName} TO {RuntimeRole};
        GRANT EXECUTE ON FUNCTION {SchemaName}.{AppendFunction}(uuid, smallint, uuid, smallint, integer, interval)
            TO {RuntimeRole};
        GRANT EXECUTE ON FUNCTION {SchemaName}.{ReadFunction}(bigint, integer)
            TO {RuntimeRole};
        """;

    public static string RoleIsolationSql { get; } = $"""
        DO $contract$
        BEGIN
            IF pg_has_role('{RuntimeRole}', '{OwnerRole}', 'MEMBER')
               OR pg_has_role('{OwnerRole}', '{RuntimeRole}', 'MEMBER')
               OR pg_has_role('{RuntimeRole}', '{MigratorRole}', 'MEMBER')
               OR pg_has_role('{MigratorRole}', '{RuntimeRole}', 'MEMBER') THEN
                RAISE EXCEPTION 'privacy erasure authority runtime role must remain separate';
            END IF;
        END
        $contract$;
        """;

    public static string FiniteRetentionRollbackSql { get; } = $"""
        REVOKE ALL ON FUNCTION {SchemaName}.{AppendFunction}(uuid, smallint, uuid, smallint, integer, interval)
            FROM PUBLIC, {RuntimeRole};
        DROP FUNCTION {SchemaName}.{AppendFunction}(uuid, smallint, uuid, smallint, integer, interval);
        GRANT EXECUTE ON FUNCTION {SchemaName}.{LegacyAppendFunction}(uuid, smallint, uuid, smallint, integer)
            TO {RuntimeRole};
        GRANT EXECUTE ON FUNCTION {SchemaName}.{ReadFunction}(bigint, integer)
            TO {RuntimeRole};
        """;

    public static string MigrationSql { get; } = $"""
        {RoleProvisioningSql}
        {FiniteRetentionAppendFunctionSql}
        {RuntimeAclSql}
        {RoleIsolationSql}
        """;
}
