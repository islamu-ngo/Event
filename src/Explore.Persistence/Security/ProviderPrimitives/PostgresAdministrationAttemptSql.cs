namespace Explore.Persistence.Security;

/// <summary>
/// SQL mechanics for one Administration owner's private epoch and physical attempt.
/// Each owner supplies its own names, admission predicates and writer inventory.
/// This builder neither executes SQL nor chooses source privileges or business effects.
/// </summary>
internal sealed class PostgresAdministrationAttemptSql(
    HumanPrincipalSourceSql sql, string sourceTable, string attemptTable, string prefix, string ownerRole)
{
    internal string CreateTables => $"""
        CREATE TABLE {sql.Name(sourceTable)} (id smallint PRIMARY KEY CHECK (id = 1), revision bigint NOT NULL CHECK (revision >= 0));
        INSERT INTO {sql.Name(sourceTable)} (id,revision) VALUES (1,0);
        CREATE TABLE {sql.Name(attemptTable)} (
            attempt bigint NOT NULL,backend integer NOT NULL,request jsonb NOT NULL,preimage jsonb NOT NULL,
            plan jsonb,postimage jsonb,result text,applied boolean NOT NULL,completed boolean NOT NULL,
            PRIMARY KEY (attempt,backend));
        """;

    internal string InstallSettlement => $"""
        CREATE CONSTRAINT TRIGGER {prefix}settlement AFTER INSERT ON {sql.Name(attemptTable)}
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION {sql.Name(prefix + "settlement")}();
        ALTER TABLE {sql.Name(attemptTable)} ENABLE ALWAYS TRIGGER {prefix}settlement;
        """;

    internal string PrivateCatalog(string migratorLogin)
    {
        var fields = new[]
        {
            (sourceTable, "id", "int2", true), (sourceTable, "revision", "int8", true),
            (attemptTable, "attempt", "int8", true), (attemptTable, "backend", "int4", true),
            (attemptTable, "request", "jsonb", true), (attemptTable, "preimage", "jsonb", true),
            (attemptTable, "plan", "jsonb", false), (attemptTable, "postimage", "jsonb", false),
            (attemptTable, "result", "text", false), (attemptTable, "applied", "bool", true),
            (attemptTable, "completed", "bool", true)
        };
        string internals = string.Join("\n", new[] { sourceTable, attemptTable }.Select(name => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({Text(sql.Name(name))})
                AND t.relkind = 'r' AND t.relpersistence = 'p' AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity
                AND t.relowner = pg_catalog.to_regrole({Text(migratorLogin)})::oid)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({Text(sql.Name(name))}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid = pg_catalog.to_regclass({Text(sql.Name(name))})
                    OR i.inhparent = pg_catalog.to_regclass({Text(sql.Name(name))}))
                OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({Text(sql.Name(name))})
                    AND a.attnum > 0 AND NOT a.attisdropped) <> {fields.Count(field => field.Item1 == name)}
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = pg_catalog.to_regclass({Text(sql.Name(name))})
                    AND NOT t.tgisinternal AND NOT ({(name == attemptTable ? $"t.tgname = '{prefix}settlement'" : "false")}))
            THEN RAISE EXCEPTION '{prefix}private_catalog_drift' USING ERRCODE = '55000'; END IF;
            """));
        string fieldChecks = string.Join("\n", fields.Select(field => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({Text(sql.Name(field.Item1))})
                AND a.attname = {Text(field.Item2)} AND a.attnum > 0 AND NOT a.attisdropped
                AND a.atttypid = 'pg_catalog.{field.Item3}'::regtype AND a.atttypmod = -1
                AND a.attnotnull = {field.Item4.ToString().ToLowerInvariant()} AND a.attgenerated = '' AND a.attidentity = ''
                AND NOT a.atthasdef AND a.attcollation = {(field.Item3 == "text" ? "'pg_catalog.\"default\"'::regcollation" : "0")})
            THEN RAISE EXCEPTION '{prefix}private_column_drift' USING ERRCODE = '55000'; END IF;
            """));
        return $"""
            {internals}
            {fieldChecks}
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = {Text(sql.Name(sourceTable))}::regclass
                AND c.contype = 'p' AND NOT c.condeferrable AND c.convalidated AND pg_catalog.pg_get_constraintdef(c.oid) = 'PRIMARY KEY (id)'
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indexrelid = c.conindid
                    AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate))
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = {Text(sql.Name(attemptTable))}::regclass
                    AND c.contype = 'p' AND NOT c.condeferrable AND c.convalidated
                    AND pg_catalog.pg_get_constraintdef(c.oid) = 'PRIMARY KEY (attempt, backend)'
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indexrelid = c.conindid
                        AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate))
                OR (SELECT count(*) FROM pg_catalog.pg_constraint c WHERE c.conrelid = {Text(sql.Name(sourceTable))}::regclass AND c.contype <> 'n') <> 3
                OR (SELECT count(*) FROM pg_catalog.pg_constraint c WHERE c.conrelid = {Text(sql.Name(attemptTable))}::regclass AND c.contype <> 'n') <> 2
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = {Text(sql.Name(sourceTable))}::regclass
                    AND c.contype = 'c' AND c.convalidated AND NOT c.connoinherit AND pg_catalog.pg_get_expr(c.conbin,c.conrelid) = '(id = 1)')
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = {Text(sql.Name(sourceTable))}::regclass
                    AND c.contype = 'c' AND c.convalidated AND NOT c.connoinherit AND pg_catalog.pg_get_expr(c.conbin,c.conrelid) = '(revision >= 0)')
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = {Text(sql.Name(attemptTable))}::regclass
                    AND t.tgname = '{prefix}settlement' AND t.tgfoid = pg_catalog.to_regprocedure({Text(sql.Name(prefix + "settlement") + "()")})
                    AND t.tgtype = 5 AND t.tgenabled = 'A' AND NOT t.tgisinternal AND t.tgqual IS NULL
                    AND t.tgnargs = 0 AND t.tgargs = ''::bytea AND t.tgattr = ''::int2vector
                    AND t.tgconstraint <> 0 AND t.tgdeferrable AND t.tginitdeferred AND t.tgoldtable IS NULL AND t.tgnewtable IS NULL)
            THEN RAISE EXCEPTION '{prefix}private_seal_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    internal string AcquireBody(string guard, string requestGuard, string caller) => $"""
        DECLARE observed jsonb;
        BEGIN
            {guard}
            {requestGuard}
            {caller}
            UPDATE {sql.Name(sourceTable)} SET revision = revision WHERE id = 1;
            IF NOT FOUND THEN RAISE EXCEPTION '{prefix}epoch_missing' USING ERRCODE = '55000'; END IF;
            observed := {sql.Name(prefix + "read")}(p_request);
            IF p_before IS DISTINCT FROM observed
            THEN RAISE EXCEPTION '{prefix}preimage_changed' USING ERRCODE = '40001'; END IF;
            INSERT INTO {sql.Name(attemptTable)}
                (attempt,backend,request,preimage,plan,postimage,result,applied,completed)
                VALUES (pg_catalog.txid_current(),pg_catalog.pg_backend_pid(),p_request,observed,NULL,NULL,NULL,false,false);
            RETURN observed;
        END;
        """;

    internal string LoadAttempt => $"""
        SELECT * INTO admitted FROM {sql.Name(attemptTable)}
            WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid() FOR UPDATE;
        IF NOT FOUND OR admitted.request IS DISTINCT FROM p_request OR admitted.completed
        THEN RAISE EXCEPTION '{prefix}original_attempt_required' USING ERRCODE = '42501'; END IF;
        """;

    /// <summary>Erase payloads before commit while retaining the transaction/backend tombstone.</summary>
    internal string CompleteBody(string guard, string requestGuard, string caller) => $"""
        DECLARE admitted {sql.Name(attemptTable)}%ROWTYPE; observed jsonb;
        BEGIN
            {guard}
            {requestGuard}
            {caller}
            {LoadAttempt}
            IF NOT admitted.applied OR admitted.plan IS NULL
            THEN RAISE EXCEPTION '{prefix}incomplete_group' USING ERRCODE = '42501'; END IF;
            observed := {sql.Name(prefix + "read")}(p_request);
            IF observed IS DISTINCT FROM p_disclosed OR observed->'Data' IS DISTINCT FROM admitted.postimage
                OR (observed->>'Revision')::bigint IS DISTINCT FROM
                    (admitted.preimage->>'Revision')::bigint + pg_catalog.jsonb_array_length(admitted.plan)
            THEN RAISE EXCEPTION '{prefix}completion_changed' USING ERRCODE = '40001'; END IF;
            UPDATE {sql.Name(attemptTable)} SET completed = true,request = pg_catalog.jsonb_build_object(),
                preimage = pg_catalog.jsonb_build_object(),plan = '[]'::jsonb,postimage = pg_catalog.jsonb_build_object(),result = NULL
                WHERE attempt = admitted.attempt AND backend = admitted.backend;
            RETURN observed;
        END;
        """;

    internal string ChangedBody(string guard, string relations) => $"""
        BEGIN
            {guard}
            IF current_user <> '{ownerRole}' OR TG_WHEN <> 'BEFORE' OR TG_LEVEL <> 'STATEMENT'
                OR TG_OP NOT IN ('INSERT','UPDATE','DELETE','TRUNCATE')
                OR TG_RELID NOT IN ({relations})
            THEN RAISE EXCEPTION '{prefix}writer_target' USING ERRCODE = '55000'; END IF;
            IF EXISTS (SELECT 1 FROM {sql.Name(attemptTable)}
                WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid() AND completed)
            THEN RAISE EXCEPTION '{prefix}write_after_completion' USING ERRCODE = '42501'; END IF;
            UPDATE {sql.Name(sourceTable)} SET revision = revision + 1 WHERE id = 1;
            IF NOT FOUND THEN RAISE EXCEPTION '{prefix}epoch_missing' USING ERRCODE = '55000'; END IF;
            RETURN NULL;
        END;
        """;

    internal string SettlementBody => $"""
        BEGIN
            IF current_user <> '{ownerRole}' OR TG_RELID <> {Text(sql.Name(attemptTable))}::regclass
                OR TG_WHEN <> 'AFTER' OR TG_LEVEL <> 'ROW' OR TG_OP <> 'INSERT'
                OR NEW.attempt <> pg_catalog.txid_current() OR NEW.backend <> pg_catalog.pg_backend_pid()
                OR NOT EXISTS (SELECT 1 FROM {sql.Name(attemptTable)} a
                    WHERE a.attempt = NEW.attempt AND a.backend = NEW.backend AND a.completed AND a.applied
                        AND a.request = pg_catalog.jsonb_build_object() AND a.preimage = pg_catalog.jsonb_build_object()
                        AND a.plan = '[]'::jsonb AND a.postimage = pg_catalog.jsonb_build_object() AND a.result IS NULL)
            THEN RAISE EXCEPTION '{prefix}private_settlement_missing' USING ERRCODE = '42501'; END IF;
            RETURN NULL;
        END;
        """;

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
