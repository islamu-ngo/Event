using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Federation;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Admits global PDS work without exposing payloads or session material.
/// The non-login function owner can read scheduling metadata and update leases only.
/// </summary>
public static class PostgresPdsQueueAdmissionContract
{
    public const string OwnerRole = "event_pds_admission_owner";
    public const string MigratorRole = "event_pds_admission_migrator";
    public const string GlobalRole = "event_pds_admission_global";
    private const string PolicyName = "pds_queue_admission";

    public static async Task ApplyAsync(
        ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task<IReadOnlyList<PdsSyncClaim>> ClaimAsync(
        ExploreDbContext context, int batchSize, string leaseOwner, DateTime claimedAt,
        TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 100 || claimedAt.Kind != DateTimeKind.Utc ||
            leaseDuration <= TimeSpan.Zero)
            return [];

        ArgumentNullException.ThrowIfNull(leaseOwner);
        string owner = leaseOwner.Trim();
        if (owner.Length is 0 or > 200)
            throw new ArgumentOutOfRangeException(nameof(leaseOwner));

        DateTime expiresAt = claimedAt.Add(leaseDuration);
        if (claimedAt == DateTime.MinValue || claimedAt == DateTime.MaxValue ||
            expiresAt == DateTime.MinValue || expiresAt == DateTime.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(claimedAt));

        var tokens = new Guid[batchSize];
        var distinctTokens = new HashSet<Guid>();
        for (int index = 0; index < tokens.Length; index++)
        {
            Guid token;
            do
            {
                token = Guid.CreateVersion7();
            }
            while (token == Guid.Empty || !distinctTokens.Add(token));
            tokens[index] = token;
        }

        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT * FROM {FunctionSql(context)}(@take, @owner, @claimed, @expires, @tokens)";
        command.Parameters.Add(new NpgsqlParameter("take", NpgsqlDbType.Integer) { Value = batchSize });
        command.Parameters.Add(new NpgsqlParameter("owner", NpgsqlDbType.Text) { Value = owner });
        command.Parameters.Add(new NpgsqlParameter("claimed", NpgsqlDbType.TimestampTz) { Value = claimedAt });
        command.Parameters.Add(new NpgsqlParameter("expires", NpgsqlDbType.TimestampTz) { Value = expiresAt });
        command.Parameters.Add(new NpgsqlParameter("tokens", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = tokens });
        var claims = new List<PdsSyncClaim>(batchSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            claims.Add(new PdsSyncClaim(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.GetGuid(3), reader.GetInt64(4)));
        return claims;
    }

    private static string Schema(ExploreDbContext context, IEntityType entity) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? entity.GetSchema() ?? "public";

    private static string FunctionSql(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(PdsSyncOutbox))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.GetTableName()! + "_admit", Schema(context, entity));
    }

    public static string BuildSql(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var queueEntity = context.Model.FindEntityType(typeof(PdsSyncOutbox))!;
        var recordEntity = context.Model.FindEntityType(typeof(AtprotoRecord))!;
        string queueSchema = Schema(context, queueEntity);
        string recordSchema = Schema(context, recordEntity);
        string queue = sql.DelimitIdentifier(queueEntity.GetTableName()!, queueSchema);
        string records = sql.DelimitIdentifier(recordEntity.GetTableName()!, recordSchema);
        string schema = sql.DelimitIdentifier(queueSchema);
        string dependencySchema = sql.DelimitIdentifier(recordSchema);
        string ColumnName(IEntityType entity, string property) =>
            entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
        string Column(IEntityType entity, string property) => sql.DelimitIdentifier(ColumnName(entity, property));
        string Q(string property) => Column(queueEntity, property);
        string R(string property) => Column(recordEntity, property);
        string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        string id = Q(nameof(PdsSyncOutbox.Id));
        string tenant = Q(nameof(PdsSyncOutbox.TenantId));
        string user = Q(nameof(PdsSyncOutbox.UserId));
        string status = Q(nameof(PdsSyncOutbox.Status));
        string created = Q(nameof(PdsSyncOutbox.CreatedAt));
        string retry = Q(nameof(PdsSyncOutbox.NextRetryAt));
        string superseded = Q(nameof(PdsSyncOutbox.SupersededAt));
        string dependency = Q(nameof(PdsSyncOutbox.DependsOnAtprotoRecordId));
        string owner = Q(nameof(PdsSyncOutbox.LeaseOwner));
        string token = Q(nameof(PdsSyncOutbox.LeaseToken));
        string expires = Q(nameof(PdsSyncOutbox.LeaseExpiresAt));
        string fence = Q(nameof(PdsSyncOutbox.LeaseFence));
        string recordId = R(nameof(AtprotoRecord.Id));
        string uri = R(nameof(AtprotoRecord.Uri));
        string cid = R(nameof(AtprotoRecord.Cid));
        string tombstoned = R(nameof(AtprotoRecord.TombstonedAt));
        string[] readProperties =
        [
            nameof(PdsSyncOutbox.Id), nameof(PdsSyncOutbox.TenantId), nameof(PdsSyncOutbox.UserId),
            nameof(PdsSyncOutbox.Status), nameof(PdsSyncOutbox.CreatedAt), nameof(PdsSyncOutbox.NextRetryAt),
            nameof(PdsSyncOutbox.SupersededAt), nameof(PdsSyncOutbox.DependsOnAtprotoRecordId),
            nameof(PdsSyncOutbox.LeaseToken), nameof(PdsSyncOutbox.LeaseExpiresAt), nameof(PdsSyncOutbox.LeaseFence)
        ];
        string[] writeProperties =
        [
            nameof(PdsSyncOutbox.Status), nameof(PdsSyncOutbox.LeaseOwner), nameof(PdsSyncOutbox.LeaseToken),
            nameof(PdsSyncOutbox.LeaseExpiresAt), nameof(PdsSyncOutbox.LeaseFence)
        ];
        string[] recordProperties =
        [nameof(AtprotoRecord.Id), nameof(AtprotoRecord.Uri), nameof(AtprotoRecord.Cid), nameof(AtprotoRecord.TombstonedAt)];
        string[] readColumns = readProperties.Select(Q).ToArray();
        string[] writeColumns = writeProperties.Select(Q).ToArray();
        string[] recordColumns = recordProperties.Select(R).ToArray();
        string Names(IEntityType entity, IEnumerable<string> properties) =>
            string.Join(", ", properties.Select(property => Literal(ColumnName(entity, property))));
        string function = FunctionSql(context);
        string signature = $"{function}(integer, text, timestamp with time zone, timestamp with time zone, uuid[])";
        long lockKey = RelationalNamedLock.ComputeStableKey("atproto-pds-outbox-claim");

        return $"""
            DO $contract$
            DECLARE v_role text;
            BEGIN
                FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{GlobalRole}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role) THEN
                        EXECUTE pg_catalog.format(
                            'CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS',
                            v_role);
                    ELSIF EXISTS (
                        SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role
                          AND (rolcanlogin OR rolsuper OR rolcreatedb OR rolcreaterole
                               OR rolinherit OR rolreplication OR rolbypassrls)) THEN
                        RAISE EXCEPTION 'unsafe PDS admission role: %', v_role;
                    END IF;
                END LOOP;
                IF EXISTS (
                    SELECT 1 FROM pg_catalog.pg_auth_members AS edge
                    JOIN pg_catalog.pg_roles AS member ON member.oid = edge.member
                    JOIN pg_catalog.pg_roles AS parent ON parent.oid = edge.roleid
                    WHERE (member.rolname IN ('{OwnerRole}', '{GlobalRole}'))
                       OR (member.rolname = '{MigratorRole}' AND parent.rolname <> '{OwnerRole}')
                       OR (parent.rolname = '{OwnerRole}' AND member.rolname <> '{MigratorRole}')
                       OR (parent.rolname = '{MigratorRole}' AND member.rolname <> CURRENT_USER)) THEN
                    RAISE EXCEPTION 'unsafe PDS admission membership';
                END IF;
                IF EXISTS (
                    WITH RECURSIVE inherited_roles(roleid) AS (
                        SELECT roleid FROM pg_catalog.pg_auth_members
                        WHERE member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                        UNION
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                        JOIN inherited_roles inherited ON edge.member = inherited.roleid)
                    SELECT 1 FROM inherited_roles
                    WHERE roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}'))
                   OR pg_catalog.has_schema_privilege('{GlobalRole}', {Literal(queueSchema)}, 'CREATE')
                   OR pg_catalog.has_schema_privilege('{GlobalRole}', {Literal(recordSchema)}, 'CREATE') THEN
                    RAISE EXCEPTION 'PDS admission global and migration authority must remain separate';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM pg_catalog.pg_class AS relation
                    WHERE relation.oid IN ({Literal(queue)}::pg_catalog.regclass, {Literal(records)}::pg_catalog.regclass)
                      AND (relation.relowner IN (
                          SELECT oid FROM pg_catalog.pg_roles
                          WHERE rolname IN ('{OwnerRole}', '{GlobalRole}'))
                          OR EXISTS (
                              SELECT 1 FROM pg_catalog.aclexplode(relation.relacl) AS privilege
                              JOIN pg_catalog.pg_roles AS role ON role.oid = privilege.grantee
                              WHERE role.rolname IN ('{OwnerRole}', '{GlobalRole}')))) THEN
                    RAISE EXCEPTION 'PDS admission roles must not own or have table-wide grants';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM pg_catalog.pg_attribute AS attribute
                    CROSS JOIN LATERAL pg_catalog.aclexplode(attribute.attacl) AS privilege
                    JOIN pg_catalog.pg_roles AS role ON role.oid = privilege.grantee
                    WHERE attribute.attrelid IN (
                        {Literal(queue)}::pg_catalog.regclass, {Literal(records)}::pg_catalog.regclass)
                      AND role.rolname IN ('{OwnerRole}', '{GlobalRole}')
                      AND (role.rolname = '{GlobalRole}' OR privilege.is_grantable OR NOT (
                          (attribute.attrelid = {Literal(queue)}::pg_catalog.regclass
                           AND ((privilege.privilege_type = 'SELECT' AND attribute.attname IN ({Names(queueEntity, readProperties)}))
                             OR (privilege.privilege_type = 'UPDATE' AND attribute.attname IN ({Names(queueEntity, writeProperties)}))))
                          OR (attribute.attrelid = {Literal(records)}::pg_catalog.regclass
                              AND privilege.privilege_type = 'SELECT'
                              AND attribute.attname IN ({Names(recordEntity, recordProperties)}))))) THEN
                    RAISE EXCEPTION 'unsafe PDS admission column grants';
                END IF;
            END $contract$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {dependencySchema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON TABLE {queue}, {records} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT ({string.Join(", ", readColumns)}) ON {queue} TO {OwnerRole};
            GRANT UPDATE ({string.Join(", ", writeColumns)}) ON {queue} TO {OwnerRole};
            GRANT SELECT ({string.Join(", ", recordColumns)}) ON {records} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}_select" ON {queue};
            CREATE POLICY "{PolicyName}_select" ON {queue}
                FOR SELECT TO {OwnerRole} USING (true);
            DROP POLICY IF EXISTS "{PolicyName}_update" ON {queue};
            CREATE POLICY "{PolicyName}_update" ON {queue}
                FOR UPDATE TO {OwnerRole} USING (true) WITH CHECK (true);
            DROP POLICY IF EXISTS "{PolicyName}_dependency" ON {records};
            CREATE POLICY "{PolicyName}_dependency" ON {records}
                FOR SELECT TO {OwnerRole} USING (true);
            DO $bootstrap$
            DECLARE v_previous_role text := pg_catalog.current_setting('role');
            BEGIN
                PERFORM pg_catalog.set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}(
                    p_take integer, p_owner text, p_claimed timestamp with time zone,
                    p_expires timestamp with time zone, p_tokens uuid[])
                RETURNS TABLE (
                    outbox_id uuid, tenant_id uuid, user_id uuid, lease_token uuid, lease_fence bigint)
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    IF p_take IS NULL OR p_take NOT BETWEEN 1 AND 100
                       OR p_owner IS NULL OR p_owner <> btrim(p_owner)
                       OR char_length(p_owner) NOT BETWEEN 1 AND 200
                       OR p_claimed IS NULL OR NOT isfinite(p_claimed)
                       OR p_expires IS NULL OR NOT isfinite(p_expires) OR p_expires <= p_claimed
                       OR p_tokens IS NULL OR array_ndims(p_tokens) IS DISTINCT FROM 1
                       OR array_lower(p_tokens, 1) IS DISTINCT FROM 1
                       OR array_upper(p_tokens, 1) IS DISTINCT FROM p_take
                       OR cardinality(p_tokens) <> p_take
                       OR EXISTS (SELECT 1 FROM unnest(p_tokens) AS supplied(value)
                           WHERE supplied.value IS NULL
                              OR supplied.value = '00000000-0000-0000-0000-000000000000'::uuid)
                       OR (SELECT count(DISTINCT supplied.value) FROM unnest(p_tokens) AS supplied(value)) <> p_take THEN
                        RAISE EXCEPTION 'invalid PDS admission bounds' USING ERRCODE = '22023';
                    END IF;
                    PERFORM pg_advisory_xact_lock({lockKey});
                    RETURN QUERY
                    WITH selected AS MATERIALIZED (
                        SELECT queue.{id} AS selected_id, queue.{created} AS selected_created
                        FROM {queue} AS queue
                        WHERE queue.{superseded} IS NULL
                          AND ((queue.{status} = {(int)PdsSyncStatus.Pending}
                                AND (queue.{retry} IS NULL OR queue.{retry} <= p_claimed))
                               OR (queue.{status} = {(int)PdsSyncStatus.Processing}
                                   AND queue.{expires} <= p_claimed))
                          AND (queue.{dependency} IS NULL OR EXISTS (
                              SELECT 1 FROM {records} AS dependency
                              WHERE dependency.{recordId} = queue.{dependency}
                                AND dependency.{uri} IS NOT NULL AND dependency.{cid} IS NOT NULL
                                AND dependency.{tombstoned} IS NULL))
                        ORDER BY queue.{created}, queue.{id}
                        LIMIT p_take FOR UPDATE OF queue
                    ), numbered AS MATERIALIZED (
                        SELECT selected.selected_id, selected.selected_created,
                            row_number() OVER (ORDER BY selected.selected_created, selected.selected_id)::integer AS ordinal
                        FROM selected
                    ), claimed AS (
                        UPDATE {queue} AS queue
                        SET {status} = {(int)PdsSyncStatus.Processing}, {owner} = p_owner,
                            {token} = p_tokens[numbered.ordinal], {expires} = p_expires,
                            {fence} = queue.{fence} + 1
                        FROM numbered WHERE queue.{id} = numbered.selected_id
                        RETURNING queue.{id} AS claimed_id, queue.{tenant} AS claimed_tenant,
                            queue.{user} AS claimed_user, queue.{token} AS claimed_token,
                            queue.{fence} AS claimed_fence
                    )
                    SELECT claimed.claimed_id, claimed.claimed_tenant, claimed.claimed_user,
                        claimed.claimed_token, claimed.claimed_fence
                    FROM claimed JOIN numbered ON numbered.selected_id = claimed.claimed_id
                    ORDER BY numbered.selected_created, numbered.selected_id;
                END;
                $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {signature} FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {signature} TO {GlobalRole};
                $acl$;
                PERFORM pg_catalog.set_config('role', v_previous_role, true);
            END $bootstrap$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            """;
    }
}
