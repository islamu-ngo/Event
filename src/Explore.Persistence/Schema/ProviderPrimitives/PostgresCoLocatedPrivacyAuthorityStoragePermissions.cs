using System.Data;
using System.Data.Common;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Explore.Persistence.Schema;

/// <summary>
/// Installs the fixed Runtime storage rights for the co-located privacy authority.
/// EF has no privilege or catalogue-attestation API; this primitive does not own
/// erasure, recovery, retention or identity workflows.
/// </summary>
public static class PostgresCoLocatedPrivacyAuthorityStoragePermissions
{
    public static async Task ApplyAsync(CoLocatedPrivacyErasureAuthorityDbContext context,
        PostgresNativeQueueEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        Table[] tables = Map(context);
        if (System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("privacy_authority_permissions_ambient_transaction");

        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
        var physical = transaction.GetDbTransaction();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var contextId = context.ContextId;
        int backend = connection.ProcessID;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        bool IsOriginal() => !interrupted && context.ContextId == contextId
            && connection.State == ConnectionState.Open && connection.ProcessID == backend
            && ReferenceEquals(connection, context.Database.GetDbConnection())
            && ReferenceEquals(transaction, context.Database.CurrentTransaction)
            && ReferenceEquals(physical, transaction.GetDbTransaction())
            && ReferenceEquals(connection, physical.Connection);
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsOriginal())
                throw new UnauthorizedAccessException("privacy_authority_permissions_owner_changed");
        }

        connection.StateChange += Changed;
        string savepoint = "privacy_authority_acl_" + Guid.CreateVersion7().ToString("N");
        try
        {
            RequireOriginal();
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                await using (var identity = CreateCommand(context, """
                    SELECT current_user = @migrator AND session_user = @migrator
                        AND pg_catalog.current_setting('role') IN ('none', session_user::text)
                        AND pg_catalog.current_setting('session_replication_role') = 'origin'
                    """))
                {
                    identity.Parameters.Add(new NpgsqlParameter("migrator", enrollment.MigratorLogin));
                    if (await identity.ExecuteScalarAsync(cancellationToken) is not true)
                        throw new UnauthorizedAccessException("privacy_authority_permissions_installer_identity");
                }
                RequireOriginal();
                // Serialize installers and retain each independently mapped relation against DDL.
                await using (var locks = CreateCommand(context,
                    $"LOCK TABLE {string.Join(", ", tables.Select(table => table.Sql).Order(StringComparer.Ordinal))} IN SHARE ROW EXCLUSIVE MODE"))
                {
                    await locks.ExecuteNonQueryAsync(cancellationToken);
                }
                RequireOriginal();
                int state;
                await using (var proof = CreateManifestCommand(context, tables, enrollment, installer: true))
                    state = (int)(await proof.ExecuteScalarAsync(cancellationToken)
                        ?? throw new InvalidOperationException("privacy_authority_permissions_proof_missing"));
                RequireOriginal();
                if (state == 0)
                {
                    var names = context.GetService<ISqlGenerationHelper>();
                    string runtime = names.DelimitIdentifier(enrollment.RuntimeLogin);
                    foreach (Table table in tables)
                    {
                        string columns = string.Join(", ", table.Columns.Select(column => names.DelimitIdentifier(column.Name)));
                        string updates = string.Join(", ", table.Columns.Where(column => column.Update)
                            .Select(column => names.DelimitIdentifier(column.Name)));
                        string sql = $"GRANT SELECT ({columns}), INSERT ({columns}) ON TABLE {table.Sql} TO {runtime};";
                        if (updates.Length != 0)
                            sql += $"\nGRANT UPDATE ({updates}) ON TABLE {table.Sql} TO {runtime};";
                        if (table.Delete)
                            sql += $"\nGRANT DELETE ON TABLE {table.Sql} TO {runtime};";
                        RequireOriginal();
                        await using var grant = CreateCommand(context, sql);
                        await grant.ExecuteNonQueryAsync(cancellationToken);
                        RequireOriginal();
                    }
                    await using var complete = CreateManifestCommand(context, tables, enrollment, installer: true);
                    state = (int)(await complete.ExecuteScalarAsync(cancellationToken)
                        ?? throw new InvalidOperationException("privacy_authority_permissions_proof_missing"));
                    RequireOriginal();
                }
                if (state != 1)
                    throw new InvalidOperationException("privacy_authority_permissions_drift");
                RequireOriginal();
                await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
            }
            catch
            {
                // Never roll back a replacement transaction or abandon grants in a shared one.
                if (IsOriginal())
                {
                    await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                    await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
                }
                throw;
            }
            if (owned is not null)
            {
                RequireOriginal();
                await owned.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            connection.StateChange -= Changed;
        }
    }

    /// <summary>
    /// Read-only admission for the original Runtime or Migrator login. Schema
    /// USAGE is an enrollment prerequisite; this contract never grants schema writes.
    /// </summary>
    public static async Task RequireReadyAsync(CoLocatedPrivacyErasureAuthorityDbContext context,
        PostgresNativeQueueEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        Table[] tables = Map(context);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        bool opened = connection.State == ConnectionState.Closed;
        if (opened) await context.Database.OpenConnectionAsync(cancellationToken);
        var contextId = context.ContextId;
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        connection.StateChange += Changed;
        try
        {
            if (connection.State != ConnectionState.Open)
                throw new UnauthorizedAccessException("privacy_authority_permissions_connection_required");
            int backend = connection.ProcessID;
            await using var proof = CreateManifestCommand(context, tables, enrollment, installer: false);
            object? result = await proof.ExecuteScalarAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (result is not 1 || interrupted || context.ContextId != contextId
                || connection.State != ConnectionState.Open || connection.ProcessID != backend
                || !ReferenceEquals(connection, context.Database.GetDbConnection())
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())
                || (transaction is not null && !ReferenceEquals(connection, transaction.Connection)))
                throw new UnauthorizedAccessException("privacy_authority_permissions_not_ready");
        }
        finally
        {
            connection.StateChange -= Changed;
            if (opened && ReferenceEquals(connection, context.Database.GetDbConnection()))
                await context.Database.CloseConnectionAsync();
        }
    }

    // One catalogue statement observes all three ACLs together: 0 = wholly absent,
    // 1 = exact complete contract, -1 = drift. A subset is never repaired by GRANT.
    // Checks are attested by exact name inventory and validation/enforcement state,
    // not by expression fingerprints. Default expressions are not fingerprinted.
    private static DbCommand CreateManifestCommand(CoLocatedPrivacyErasureAuthorityDbContext context,
        Table[] tables, PostgresNativeQueueEnrollment enrollment, bool installer)
    {
        var command = CreateCommand(context, string.Empty);
        command.Parameters.Add(new NpgsqlParameter("migrator", enrollment.MigratorLogin));
        command.Parameters.Add(new NpgsqlParameter("runtime", enrollment.RuntimeLogin));
        command.Parameters.Add(new NpgsqlParameter("other_logins", new[]
        {
            enrollment.GlobalLogin, enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin
        }));
        var columnRows = new List<string>();
        var tableRows = new List<string>();
        var keyProofs = new List<string>();
        for (int index = 0; index < tables.Length; index++)
        {
            Table table = tables[index];
            command.Parameters.Add(new NpgsqlParameter($"table{index}", table.Sql));
            command.Parameters.Add(new NpgsqlParameter($"schema{index}", table.Schema));
            command.Parameters.Add(new NpgsqlParameter($"columns{index}", table.Columns.Select(column => column.Name).ToArray()));
            command.Parameters.Add(new NpgsqlParameter($"types{index}", table.Columns.Select(column => column.StoreType).ToArray()));
            command.Parameters.Add(new NpgsqlParameter($"required{index}", table.Columns.Select(column => !column.Nullable).ToArray()));
            command.Parameters.Add(new NpgsqlParameter($"updates{index}", table.Columns.Select(column => column.Update).ToArray()));
            command.Parameters.Add(new NpgsqlParameter($"key{index}", table.PrimaryKey));
            command.Parameters.Add(new NpgsqlParameter($"checks{index}", table.Checks));
            tableRows.Add($"(@table{index}::text, @schema{index}::text)");
            columnRows.Add($"""
                SELECT @table{index}::text, columns.* FROM
                    unnest(@columns{index}::text[], @types{index}::text[], @required{index}::boolean[], @updates{index}::boolean[])
                    AS columns(column_name, store_type, required, can_update)
                """);
            keyProofs.Add($"""
                (SELECT count(*) = 1 FROM pg_catalog.pg_constraint k
                    WHERE k.conrelid = pg_catalog.to_regclass(@table{index}) AND k.contype = 'p')
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_constraint k
                    JOIN pg_catalog.pg_index i ON i.indexrelid = k.conindid
                    WHERE k.conrelid = pg_catalog.to_regclass(@table{index}) AND k.contype = 'p'
                        AND i.indisprimary AND i.indisunique AND i.indisvalid AND i.indisready AND i.indislive
                        AND i.indpred IS NULL AND i.indexprs IS NULL
                        AND i.indnatts = cardinality(@key{index}::text[])
                        AND i.indnkeyatts = cardinality(@key{index}::text[])
                        AND ARRAY(SELECT a.attname::text FROM unnest(k.conkey) WITH ORDINALITY key(attnum, position)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = k.conrelid AND a.attnum = key.attnum
                            ORDER BY key.position) = @key{index}::text[])
                AND (SELECT count(*) = cardinality(@checks{index}::text[]) FROM pg_catalog.pg_constraint k
                    WHERE k.conrelid = pg_catalog.to_regclass(@table{index}) AND k.contype = 'c')
                AND NOT EXISTS (SELECT 1 FROM unnest(@checks{index}::text[]) expected(name)
                    WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint k
                        WHERE k.conrelid = pg_catalog.to_regclass(@table{index})
                            AND k.contype = 'c' AND k.conname = expected.name))
                """);
        }
        command.Parameters.Add(new NpgsqlParameter("intent_unique", tables[1].UniqueKey));
        command.Parameters.Add(new NpgsqlParameter("fence_sequence", tables[2].PrimaryKey[0]));
        command.Parameters.Add(new NpgsqlParameter("intent_sequence", tables[1].PrimaryKey[0]));
        command.CommandText = $"""
            WITH sources(relation, schema_name) AS (VALUES {string.Join(", ", tableRows)}),
            relations AS (
                SELECT s.*, c.oid, c.relowner, c.relacl, c.relkind, c.relpersistence,
                    c.relrowsecurity, c.relforcerowsecurity, c.relispartition
                FROM sources s LEFT JOIN pg_catalog.pg_class c ON c.oid = pg_catalog.to_regclass(s.relation)),
            owner_role AS (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = @migrator AND rolcanlogin),
            runtime_role AS (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = @runtime AND rolcanlogin),
            expected_columns(relation, column_name, store_type, required, can_update) AS (
                {string.Join("\nUNION ALL\n", columnRows)}),
            expected_grants AS (
                SELECT relation, column_name, privilege_type FROM expected_columns
                    CROSS JOIN unnest(ARRAY['SELECT','INSERT']::text[]) privilege_type
                UNION ALL SELECT relation, column_name, 'UPDATE' FROM expected_columns WHERE can_update
                UNION ALL SELECT @table1::text, NULL::text, 'DELETE'),
            actual_grants AS (
                SELECT r.relation, NULL::text AS column_name, acl.*
                FROM relations r CROSS JOIN LATERAL
                    pg_catalog.aclexplode(COALESCE(r.relacl, pg_catalog.acldefault('r', r.relowner))) acl
                WHERE acl.grantee <> r.relowner
                UNION ALL
                SELECT r.relation, a.attname::text, acl.*
                FROM relations r JOIN pg_catalog.pg_attribute a ON a.attrelid = r.oid
                    CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attnum <> 0 AND NOT a.attisdropped AND acl.grantee <> r.relowner)
            SELECT CASE WHEN
                current_user = session_user
                AND {(installer ? "session_user = @migrator" : "session_user IN (@migrator, @runtime)")}
                AND pg_catalog.current_setting('role') IN ('none', session_user::text)
                AND pg_catalog.current_setting('session_replication_role') = 'origin'
                AND EXISTS (SELECT 1 FROM owner_role) AND EXISTS (SELECT 1 FROM runtime_role)
                AND NOT EXISTS (SELECT 1 FROM relations r
                    WHERE r.oid IS NULL OR r.relkind <> 'r' OR r.relpersistence <> 'p'
                        OR r.relowner <> (SELECT oid FROM owner_role)
                        OR r.relrowsecurity OR r.relforcerowsecurity OR r.relispartition)
                AND NOT EXISTS (SELECT 1 FROM relations r WHERE
                    NOT pg_catalog.has_schema_privilege((SELECT oid FROM runtime_role), r.schema_name, 'USAGE'))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r CROSS JOIN runtime_role runtime
                    WHERE pg_catalog.pg_has_role(runtime.oid, r.oid, 'MEMBER') AND (
                        r.oid = (SELECT oid FROM owner_role) OR r.rolsuper OR r.rolbypassrls
                        OR r.rolcreaterole OR r.rolcreatedb OR r.rolreplication
                        OR r.rolname IN ('pg_read_all_data', 'pg_write_all_data')
                        OR EXISTS (SELECT 1 FROM sources s WHERE pg_catalog.has_schema_privilege(r.oid, s.schema_name, 'CREATE'))))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members
                    WHERE roleid = (SELECT oid FROM runtime_role))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                    WHERE r.rolname = ANY(@other_logins) AND (
                        r.rolsuper OR r.rolcreaterole OR r.rolcreatedb OR r.rolreplication OR r.rolbypassrls))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r CROSS JOIN relations t
                    WHERE r.oid NOT IN ((SELECT oid FROM owner_role), (SELECT oid FROM runtime_role))
                        AND NOT r.rolsuper AND (
                            pg_catalog.pg_has_role(r.oid, (SELECT oid FROM owner_role), 'MEMBER')
                            OR (r.rolcanlogin AND (
                                pg_catalog.has_table_privilege(r.oid, t.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                                OR pg_catalog.has_any_column_privilege(r.oid, t.oid, 'SELECT,INSERT,UPDATE,REFERENCES')
                                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles inherited
                                    WHERE pg_catalog.pg_has_role(r.oid, inherited.oid, 'MEMBER')
                                        AND (inherited.rolsuper OR inherited.rolname IN ('pg_read_all_data', 'pg_write_all_data')))))))
                AND NOT EXISTS (SELECT 1 FROM expected_columns e JOIN relations r USING (relation)
                    LEFT JOIN pg_catalog.pg_attribute a ON a.attrelid = r.oid AND a.attname = e.column_name
                        AND a.attnum > 0 AND NOT a.attisdropped
                    WHERE a.attnum IS NULL OR pg_catalog.format_type(a.atttypid, a.atttypmod) <> e.store_type
                        OR a.attnotnull <> e.required OR a.attidentity <> '' OR a.attgenerated <> '')
                AND NOT EXISTS (SELECT 1 FROM relations r JOIN pg_catalog.pg_attribute a ON a.attrelid = r.oid
                    WHERE a.attnum > 0 AND NOT a.attisdropped AND NOT EXISTS (
                        SELECT 1 FROM expected_columns e WHERE e.relation = r.relation AND e.column_name = a.attname))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits
                    WHERE inhrelid IN (SELECT oid FROM relations) OR inhparent IN (SELECT oid FROM relations))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy WHERE polrelid IN (SELECT oid FROM relations))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite WHERE ev_class IN (SELECT oid FROM relations))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger WHERE tgrelid IN (SELECT oid FROM relations)
                    AND (NOT tgisinternal OR tgenabled NOT IN ('O','A')))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint k WHERE k.conrelid IN (SELECT oid FROM relations)
                    AND (k.contype NOT IN ('p','c','f','n') OR NOT k.convalidated
                        OR NOT COALESCE((pg_catalog.to_jsonb(k)->>'conenforced')::boolean, true)
                        OR NOT k.conislocal OR k.coninhcount <> 0 OR k.condeferrable OR k.condeferred
                        OR (k.contype = 'c' AND k.connoinherit)
                        OR (k.contype = 'n' AND (cardinality(k.conkey) <> 1 OR NOT EXISTS (
                            SELECT 1 FROM pg_catalog.pg_attribute a
                            WHERE a.attrelid = k.conrelid AND a.attnum = k.conkey[1]
                                AND a.attnum > 0 AND NOT a.attisdropped AND a.attnotnull)))))
                AND {string.Join("\nAND ", keyProofs)}
                AND (SELECT count(*) = 1 FROM pg_catalog.pg_constraint
                    WHERE conrelid IN (SELECT oid FROM relations) AND contype = 'f')
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint
                    WHERE confrelid IN (SELECT oid FROM relations) AND contype = 'f'
                        AND conrelid NOT IN (SELECT oid FROM relations))
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_constraint f
                    WHERE f.conrelid = pg_catalog.to_regclass(@table2) AND f.contype = 'f'
                        AND f.confrelid = pg_catalog.to_regclass(@table1)
                        AND f.confmatchtype = 's' AND f.confupdtype = 'a' AND f.confdeltype = 'c'
                        AND ARRAY(SELECT a.attname::text FROM unnest(f.conkey) WITH ORDINALITY key(attnum, position)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = f.conrelid AND a.attnum = key.attnum
                            ORDER BY key.position) = ARRAY[@fence_sequence::text]
                        AND ARRAY(SELECT a.attname::text FROM unnest(f.confkey) WITH ORDINALITY key(attnum, position)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = f.confrelid AND a.attnum = key.attnum
                            ORDER BY key.position) = ARRAY[@intent_sequence::text]
                        AND (SELECT count(*) = 4 FROM pg_catalog.pg_trigger WHERE tgconstraint = f.oid AND tgisinternal))
                AND (SELECT count(*) = 4 FROM pg_catalog.pg_index WHERE indrelid IN (SELECT oid FROM relations) AND indisunique)
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indrelid = pg_catalog.to_regclass(@table1)
                    AND i.indisunique AND NOT i.indisprimary AND i.indisvalid AND i.indisready AND i.indislive
                    AND i.indimmediate AND i.indpred IS NULL AND i.indexprs IS NULL
                    AND i.indnatts = 3 AND i.indnkeyatts = 3
                    AND ARRAY(SELECT a.attname::text FROM unnest(i.indkey) WITH ORDINALITY key(attnum, position)
                        JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = key.attnum
                        ORDER BY key.position) = @intent_unique::text[])
                AND NOT EXISTS (SELECT 1 FROM actual_grants a WHERE
                    a.is_grantable OR a.grantor <> (SELECT oid FROM owner_role)
                    OR a.grantee <> (SELECT oid FROM runtime_role)
                    OR NOT EXISTS (SELECT 1 FROM expected_grants e WHERE e.relation = a.relation
                        AND e.column_name IS NOT DISTINCT FROM a.column_name AND e.privilege_type = a.privilege_type))
            THEN CASE
                WHEN NOT EXISTS (SELECT 1 FROM actual_grants) THEN 0
                WHEN NOT EXISTS (SELECT 1 FROM expected_grants e WHERE NOT EXISTS (
                    SELECT 1 FROM actual_grants a WHERE a.relation = e.relation
                        AND a.column_name IS NOT DISTINCT FROM e.column_name AND a.privilege_type = e.privilege_type)) THEN 1
                ELSE -1 END
            ELSE -1 END
            """;
        return command;
    }

    private static Table[] Map(CoLocatedPrivacyErasureAuthorityDbContext context)
    {
        Table[] tables =
        [
            MapTable(typeof(PrivacyErasureCounter),
            [
                (nameof(PrivacyErasureCounter.Singleton), "boolean", false, false),
                (nameof(PrivacyErasureCounter.StoreId), "uuid", false, false),
                (nameof(PrivacyErasureCounter.Revision), "bigint", false, true),
                (nameof(PrivacyErasureCounter.RecoveryGeneration), "bigint", false, false),
                (nameof(PrivacyErasureCounter.LastSequence), "bigint", false, true),
                (nameof(PrivacyErasureCounter.RetainedFloorSequence), "bigint", false, true),
                (nameof(PrivacyErasureCounter.IdentityKeyId), "character varying(64)", true, true),
                (nameof(PrivacyErasureCounter.IdentityKeyVerificationTag), "character varying(64)", true, true)
            ],
            [nameof(PrivacyErasureCounter.Singleton)],
            [
                "ck_privacy_erasure_authority_counter_singleton", "ck_privacy_erasure_authority_counter_nonnegative",
                "ck_privacy_erasure_authority_counter_recovery", "ck_privacy_erasure_authority_counter_retained_floor"
            ], [], delete: false),
            MapTable(typeof(PrivacyErasureIntent),
            [
                (nameof(PrivacyErasureIntent.AuthoritySequence), "bigint", false, false),
                (nameof(PrivacyErasureIntent.IntentId), "uuid", false, true),
                (nameof(PrivacyErasureIntent.SubjectKind), "smallint", false, false),
                (nameof(PrivacyErasureIntent.SubjectId), "uuid", false, true),
                (nameof(PrivacyErasureIntent.ReasonCode), "smallint", false, false),
                (nameof(PrivacyErasureIntent.PolicyVersion), "integer", false, false),
                (nameof(PrivacyErasureIntent.RequestedAtUtc), "timestamp with time zone", false, false),
                (nameof(PrivacyErasureIntent.RecordedAtUtc), "timestamp with time zone", false, false),
                (nameof(PrivacyErasureIntent.RetentionExpiresAtUtc), "timestamp with time zone", false, false),
                (nameof(PrivacyErasureIntent.IsLegalHoldPseudonymized), "boolean", false, true)
            ],
            [nameof(PrivacyErasureIntent.AuthoritySequence)],
            [
                "ck_privacy_erasure_intents_sequence", "ck_privacy_erasure_intents_intent_uuid_v7",
                "ck_privacy_erasure_intents_intent_rfc4122_variant", "ck_privacy_erasure_intents_subject_kind",
                "ck_privacy_erasure_intents_subject_nonempty", "ck_privacy_erasure_intents_reason",
                "ck_privacy_erasure_intents_policy_version", "ck_privacy_erasure_intents_server_time_order",
                "ck_privacy_erasure_intents_retention"
            ],
            [nameof(PrivacyErasureIntent.IntentId), nameof(PrivacyErasureIntent.SubjectKind), nameof(PrivacyErasureIntent.PolicyVersion)],
            delete: true),
            MapTable(typeof(PrivacyErasureIdentityFence),
            [
                (nameof(PrivacyErasureIdentityFence.AuthoritySequence), "bigint", false, false),
                (nameof(PrivacyErasureIdentityFence.IdentityKind), "integer", false, false),
                (nameof(PrivacyErasureIdentityFence.KeyId), "character varying(64)", false, false),
                (nameof(PrivacyErasureIdentityFence.Fingerprint), "character varying(64)", false, false),
                (nameof(PrivacyErasureIdentityFence.RetentionExpiresAtUtc), "timestamp with time zone", false, false)
            ],
            [
                nameof(PrivacyErasureIdentityFence.AuthoritySequence), nameof(PrivacyErasureIdentityFence.IdentityKind),
                nameof(PrivacyErasureIdentityFence.KeyId), nameof(PrivacyErasureIdentityFence.Fingerprint)
            ],
            ["ck_identity_fences_kind", "ck_identity_fences_key", "ck_identity_fences_digest"], [], delete: false)
        ];
        if (tables.Select(table => table.Sql).Distinct(StringComparer.Ordinal).Count() != 3
            || context.Model.GetEntityTypes().Count() != 3)
            throw new InvalidOperationException("privacy_authority_permissions_model_drift");
        return tables;

        Table MapTable(Type type, (string Property, string StoreType, bool Nullable, bool Update)[] columns,
            string[] key, string[] checks, string[] uniqueKey, bool delete)
        {
            var entity = context.Model.FindEntityType(type)
                ?? throw new InvalidOperationException("privacy_authority_permissions_mapping_missing");
            var design = context.GetService<IDesignTimeModel>().Model.FindEntityType(type)
                ?? throw new InvalidOperationException("privacy_authority_permissions_mapping_missing");
            string name = entity.GetTableName()
                ?? throw new InvalidOperationException("privacy_authority_permissions_mapping_missing");
            var store = StoreObjectIdentifier.Table(name, entity.GetSchema());
            var options = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>();
            string schema = entity.GetSchema() ?? "public";
            if (options is not null && schema == options.ModelSchema) schema = options.TargetSchema;
            var properties = entity.GetProperties().ToArray();
            var foreignKeys = entity.GetForeignKeys().ToArray();
            var uniqueIndexes = entity.GetIndexes().Where(index => index.IsUnique).ToArray();
            if (entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.IsOwned()
                || entity.GetComplexProperties().Any() || entity.GetSkipNavigations().Any()
                || properties.Length != columns.Length
                || !properties.Select(property => property.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(columns.Select(column => column.Property).Order(StringComparer.Ordinal))
                || entity.GetKeys().Count() != 1
                || entity.FindPrimaryKey() is not { } primaryKey
                || !primaryKey.Properties.Select(property => property.Name).SequenceEqual(key)
                || !design.GetCheckConstraints().Select(check => check.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(checks.Order(StringComparer.Ordinal))
                || (uniqueKey.Length == 0 ? uniqueIndexes.Length != 0
                    : uniqueIndexes.Length != 1 || !uniqueIndexes[0].Properties.Select(property => property.Name).SequenceEqual(uniqueKey))
                || (type != typeof(PrivacyErasureIdentityFence) ? foreignKeys.Length != 0
                    : foreignKeys.Length != 1 || foreignKeys[0].PrincipalEntityType.ClrType != typeof(PrivacyErasureIntent)
                        || foreignKeys[0].DeleteBehavior != DeleteBehavior.Cascade || !foreignKeys[0].IsRequired
                        || foreignKeys[0].IsUnique
                        || !foreignKeys[0].Properties.Select(property => property.Name)
                            .SequenceEqual([nameof(PrivacyErasureIdentityFence.AuthoritySequence)])
                        || !foreignKeys[0].PrincipalKey.Properties.Select(property => property.Name)
                            .SequenceEqual([nameof(PrivacyErasureIntent.AuthoritySequence)])))
                throw new InvalidOperationException("privacy_authority_permissions_model_drift");

            Column[] mapped = columns.Select(column =>
            {
                var property = entity.FindProperty(column.Property)!;
                string columnName = property.GetColumnName(store)
                    ?? throw new InvalidOperationException("privacy_authority_permissions_mapping_missing");
                if (property.IsNullable != column.Nullable || property.GetRelationalTypeMapping().StoreType != column.StoreType)
                    throw new InvalidOperationException("privacy_authority_permissions_model_drift");
                return new Column(columnName, column.StoreType, column.Nullable, column.Update);
            }).ToArray();
            if (mapped.Select(column => column.Name).Distinct(StringComparer.Ordinal).Count() != columns.Length)
                throw new InvalidOperationException("privacy_authority_permissions_model_drift");
            string ColumnName(string property) => entity.FindProperty(property)!.GetColumnName(store)!;
            return new Table(context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, schema), schema,
                mapped, key.Select(ColumnName).ToArray(), checks, uniqueKey.Select(ColumnName).ToArray(), delete);
        }
    }

    private static DbCommand CreateCommand(CoLocatedPrivacyErasureAuthorityDbContext context, string sql)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? 30;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }

    private sealed record Column(string Name, string StoreType, bool Nullable, bool Update);
    private sealed record Table(string Sql, string Schema, Column[] Columns, string[] PrimaryKey,
        string[] Checks, string[] UniqueKey, bool Delete);
}
