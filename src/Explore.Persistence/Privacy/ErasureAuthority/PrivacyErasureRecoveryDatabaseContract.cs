namespace Explore.Persistence.Privacy.ErasureAuthority;

/// <summary>Function-only acquisition of the same row locked by the retained identity writer.</summary>
public static class PrivacyErasureRecoveryDatabaseContract
{
    private const string Schema = PrivacyErasureAuthorityDatabaseContract.SchemaName;
    private const string Owner = PrivacyErasureAuthorityDatabaseContract.OwnerRole;
    private const string Runtime = PrivacyErasureAuthorityDatabaseContract.RuntimeRole;

    public static string MigrationSql => $"""
        CREATE OR REPLACE FUNCTION {Schema}.read_locked_recovery_state()
        RETURNS TABLE (
            store_id uuid, revision bigint, recovery_generation bigint,
            high_water_sequence bigint, retained_floor_sequence bigint,
            identity_key_id text, identity_key_verification_tag text)
        LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, {Schema}
        AS $function$
        BEGIN
            RETURN QUERY SELECT counter.store_id, counter.revision, counter.recovery_generation,
                counter.last_sequence, counter.retained_floor_sequence,
                counter.identity_key_id::text, counter.identity_key_verification_tag::text
                FROM {Schema}.authority_counter AS counter WHERE counter.singleton FOR UPDATE;
            IF NOT FOUND THEN
                RAISE EXCEPTION 'privacy_erasure_recovery_state_unavailable' USING ERRCODE = '55000';
            END IF;
        END;
        $function$;
        ALTER FUNCTION {Schema}.read_locked_recovery_state() OWNER TO {Owner};
        REVOKE ALL ON FUNCTION {Schema}.read_locked_recovery_state() FROM PUBLIC, {Runtime};
        GRANT EXECUTE ON FUNCTION {Schema}.read_locked_recovery_state() TO {Runtime};
        """;
}
