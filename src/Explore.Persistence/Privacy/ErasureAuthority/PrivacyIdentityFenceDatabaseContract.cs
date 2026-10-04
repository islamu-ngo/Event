namespace Explore.Persistence.Privacy.ErasureAuthority;

/// <summary>Function-only runtime access to the retained identity index and serialization row.</summary>
public static class PrivacyIdentityFenceDatabaseContract
{
    private const string Schema = PrivacyErasureAuthorityDatabaseContract.SchemaName;
    private const string Owner = PrivacyErasureAuthorityDatabaseContract.OwnerRole;
    private const string Runtime = PrivacyErasureAuthorityDatabaseContract.RuntimeRole;

    public static string MigrationSql => $"""
        ALTER TABLE {Schema}.identity_fences OWNER TO {Owner};
        REVOKE ALL ON {Schema}.identity_fences FROM PUBLIC, {Runtime};

        CREATE OR REPLACE FUNCTION {Schema}.read_identity_key_state()
        RETURNS TABLE (identity_key_id text, identity_key_verification_tag text)
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, {Schema}
        AS $function$
            SELECT identity_key_id::text, identity_key_verification_tag::text
                FROM {Schema}.authority_counter WHERE singleton;
        $function$;

        CREATE OR REPLACE FUNCTION {Schema}.is_identity_subject_fenced(p_subject_id uuid)
        RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, {Schema}
        AS $function$
            SELECT EXISTS (SELECT 1 FROM {Schema}.erasure_intents WHERE subject_kind = 1
                AND subject_id = p_subject_id AND NOT is_legal_hold_pseudonymized);
        $function$;

        CREATE OR REPLACE FUNCTION {Schema}.lock_identity_fence(p_key_id text, p_verification_tag text)
        RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, {Schema}
        AS $function$
        DECLARE v_counter {Schema}.authority_counter%ROWTYPE;
        BEGIN
            SELECT * INTO STRICT v_counter FROM {Schema}.authority_counter WHERE singleton FOR UPDATE;
            IF v_counter.identity_key_id IS NULL AND EXISTS (SELECT 1 FROM {Schema}.identity_fences) THEN
                RAISE EXCEPTION 'privacy_identity_fence_authority_state_unavailable' USING ERRCODE = '55000';
            END IF;
            IF p_key_id IS NULL AND p_verification_tag IS NULL THEN RETURN; END IF;
            IF p_key_id IS NULL OR length(p_key_id) NOT BETWEEN 1 AND 64 OR p_key_id !~ '^[A-Za-z0-9_-]+$'
                OR p_verification_tag IS NULL OR length(p_verification_tag) <> 64 OR p_verification_tag !~ '^[0-9a-f]+$' THEN
                RAISE EXCEPTION 'privacy_identity_fence_key_invalid' USING ERRCODE = '22023';
            END IF;
            IF v_counter.identity_key_id IS NOT NULL AND
                (v_counter.identity_key_id <> p_key_id OR v_counter.identity_key_verification_tag <> p_verification_tag) THEN
                RAISE EXCEPTION 'privacy_identity_fence_key_mismatch' USING ERRCODE = '22023';
            END IF;
            UPDATE {Schema}.authority_counter SET identity_key_id = p_key_id,
                identity_key_verification_tag = p_verification_tag WHERE singleton;
        END;
        $function$;

        CREATE OR REPLACE FUNCTION {Schema}.append_identity_fenced_erasure(
            p_intent_id uuid, p_subject_kind smallint, p_subject_id uuid, p_reason_code smallint,
            p_policy_version integer, p_retention interval, p_key_id text, p_verification_tag text,
            p_fences jsonb)
        RETURNS SETOF {Schema}.erasure_intents
        LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, {Schema}
        AS $function$
        DECLARE v_existing {Schema}.erasure_intents%ROWTYPE; v_sequence bigint; v_expiry timestamptz;
        BEGIN
            PERFORM {Schema}.lock_identity_fence(p_key_id, p_verification_tag);
            IF p_fences IS NULL OR jsonb_typeof(p_fences) <> 'array' THEN
                RAISE EXCEPTION 'privacy_identity_fence_payload_invalid' USING ERRCODE = '22023';
            END IF;
            IF EXISTS (SELECT 1 FROM jsonb_to_recordset(p_fences) AS f(kind integer, key_id text, fingerprint text)
                WHERE kind IS NULL OR kind NOT BETWEEN 1 AND 5 OR key_id IS DISTINCT FROM p_key_id
                    OR fingerprint IS NULL OR length(fingerprint) <> 64 OR fingerprint !~ '^[0-9a-f]+$')
                OR (jsonb_array_length(p_fences) > 0 AND p_key_id IS NULL) THEN
                RAISE EXCEPTION 'privacy_identity_fence_payload_invalid' USING ERRCODE = '22023';
            END IF;
            SELECT * INTO v_existing FROM {Schema}.erasure_intents WHERE intent_id = p_intent_id;
            IF FOUND THEN
                IF EXISTS (
                    (SELECT f.identity_kind, f.key_id, f.fingerprint FROM {Schema}.identity_fences f
                        WHERE f.authority_sequence = v_existing.authority_sequence
                     EXCEPT SELECT kind, key_id, fingerprint FROM jsonb_to_recordset(p_fences)
                        AS f(kind integer, key_id text, fingerprint text))
                    UNION ALL
                    (SELECT kind, key_id, fingerprint FROM jsonb_to_recordset(p_fences)
                        AS f(kind integer, key_id text, fingerprint text)
                     EXCEPT SELECT f.identity_kind, f.key_id, f.fingerprint FROM {Schema}.identity_fences f
                        WHERE f.authority_sequence = v_existing.authority_sequence)) THEN
                    RAISE EXCEPTION 'privacy_identity_fence_payload_mismatch' USING ERRCODE = '22023';
                END IF;
            END IF;
            SELECT authority_sequence, retention_expires_at_utc INTO v_sequence, v_expiry
                FROM {Schema}.{PrivacyErasureAuthorityDatabaseContract.AppendFunction}(
                    p_intent_id, p_subject_kind, p_subject_id, p_reason_code, p_policy_version, p_retention);
            IF v_existing.authority_sequence IS NULL THEN
                INSERT INTO {Schema}.identity_fences
                    (authority_sequence, identity_kind, key_id, fingerprint, retention_expires_at_utc)
                SELECT DISTINCT v_sequence, kind, key_id, fingerprint, v_expiry
                    FROM jsonb_to_recordset(p_fences) AS f(kind integer, key_id text, fingerprint text);
            END IF;
            RETURN QUERY SELECT * FROM {Schema}.erasure_intents WHERE authority_sequence = v_sequence;
        END;
        $function$;

        CREATE OR REPLACE FUNCTION {Schema}.find_identity_fence(p_kind integer, p_key_id text, p_fingerprint text)
        RETURNS SETOF {Schema}.erasure_intents
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, {Schema}
        AS $function$
            SELECT i.* FROM {Schema}.identity_fences f
                JOIN {Schema}.erasure_intents i USING (authority_sequence)
                WHERE f.identity_kind = p_kind AND f.key_id = p_key_id AND f.fingerprint = p_fingerprint
                ORDER BY i.authority_sequence LIMIT 1;
        $function$;

        CREATE OR REPLACE FUNCTION {Schema}.read_identity_fences(p_sequence bigint)
        RETURNS SETOF {Schema}.identity_fences
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, {Schema}
        AS $function$
            SELECT * FROM {Schema}.identity_fences WHERE authority_sequence = p_sequence;
        $function$;

        CREATE OR REPLACE FUNCTION {Schema}.read_identity_fenced_intents_after(p_after bigint, p_limit integer)
        RETURNS SETOF {Schema}.erasure_intents
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, {Schema}
        AS $function$
            SELECT i.* FROM {Schema}.{PrivacyErasureAuthorityDatabaseContract.ReadFunction}(p_after, p_limit) r
                JOIN {Schema}.erasure_intents i USING (authority_sequence) ORDER BY i.authority_sequence;
        $function$;

        ALTER FUNCTION {Schema}.lock_identity_fence(text, text) OWNER TO {Owner};
        ALTER FUNCTION {Schema}.read_identity_key_state() OWNER TO {Owner};
        ALTER FUNCTION {Schema}.is_identity_subject_fenced(uuid) OWNER TO {Owner};
        ALTER FUNCTION {Schema}.append_identity_fenced_erasure(uuid, smallint, uuid, smallint, integer, interval, text, text, jsonb) OWNER TO {Owner};
        ALTER FUNCTION {Schema}.find_identity_fence(integer, text, text) OWNER TO {Owner};
        ALTER FUNCTION {Schema}.read_identity_fences(bigint) OWNER TO {Owner};
        ALTER FUNCTION {Schema}.read_identity_fenced_intents_after(bigint, integer) OWNER TO {Owner};
        REVOKE ALL ON FUNCTION {Schema}.lock_identity_fence(text, text) FROM PUBLIC;
        REVOKE ALL ON FUNCTION {Schema}.read_identity_key_state() FROM PUBLIC;
        REVOKE ALL ON FUNCTION {Schema}.is_identity_subject_fenced(uuid) FROM PUBLIC;
        REVOKE ALL ON FUNCTION {Schema}.append_identity_fenced_erasure(uuid, smallint, uuid, smallint, integer, interval, text, text, jsonb) FROM PUBLIC;
        REVOKE ALL ON FUNCTION {Schema}.find_identity_fence(integer, text, text) FROM PUBLIC;
        REVOKE ALL ON FUNCTION {Schema}.read_identity_fences(bigint) FROM PUBLIC;
        REVOKE ALL ON FUNCTION {Schema}.read_identity_fenced_intents_after(bigint, integer) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION {Schema}.lock_identity_fence(text, text) TO {Runtime};
        GRANT EXECUTE ON FUNCTION {Schema}.read_identity_key_state() TO {Runtime}, {PrivacyErasureAuthorityDatabaseContract.MigratorRole};
        GRANT EXECUTE ON FUNCTION {Schema}.is_identity_subject_fenced(uuid) TO {Runtime};
        GRANT EXECUTE ON FUNCTION {Schema}.append_identity_fenced_erasure(uuid, smallint, uuid, smallint, integer, interval, text, text, jsonb) TO {Runtime};
        GRANT EXECUTE ON FUNCTION {Schema}.find_identity_fence(integer, text, text) TO {Runtime};
        GRANT EXECUTE ON FUNCTION {Schema}.read_identity_fences(bigint) TO {Runtime};
        GRANT EXECUTE ON FUNCTION {Schema}.read_identity_fenced_intents_after(bigint, integer) TO {Runtime};
        REVOKE EXECUTE ON FUNCTION {Schema}.{PrivacyErasureAuthorityDatabaseContract.AppendFunction}(uuid, smallint, uuid, smallint, integer, interval) FROM {Runtime};
        """;
}
