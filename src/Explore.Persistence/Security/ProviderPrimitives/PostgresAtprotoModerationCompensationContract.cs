using System.Data;
using System.Text.Json;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using SourceEvidence = Explore.Persistence.Services.AtprotoModerationCompensationStore.SourceEvidence;

namespace Explore.Persistence.Security;

/// <summary>
/// Installs execute-only, subject-grounded moderation operations. Its non-login
/// owner receives only evidence columns and fixed outbox insert/supersession rights.
/// </summary>
public static class PostgresAtprotoModerationCompensationContract
{
    public const string OwnerRole = "event_atproto_moderation_owner";
    public const string MigratorRole = "event_atproto_moderation_migrator";
    public const string GlobalRole = "event_atproto_moderation_global";
    public const string PolicyName = "atproto_moderation_compensation";

    internal static void RequireEnrollment(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment)
    {
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null)
            throw new UnauthorizedAccessException("federation_settings_enrollment_required");
        enrollment.Validate();
    }

    public static string FunctionSql(ExploreDbContext context, string operation) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            "atproto_moderation_compensation_" + operation, Schema(context, typeof(PdsSyncOutbox)));

    public static async Task ApplyAsync(
        ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task<IReadOnlyList<SourceEvidence>?> TryReadPageAsync(
        ExploreDbContext context, AtprotoModerationTarget target, long offset,
        CancellationToken cancellationToken) =>
        context.Database.IsNpgsql() ? await ReadPageAsync(context, target, offset, cancellationToken) : null;

    internal static async Task<bool> TryWriteAsync(
        ExploreDbContext context, AtprotoModerationTarget target, AtprotoModerationDeleteWrite write,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql()) return false;
        await WriteAsync(context, target, write, cancellationToken);
        return true;
    }

    internal static async Task<IReadOnlyList<SourceEvidence>> ReadPageAsync(
        ExploreDbContext context, AtprotoModerationTarget target, long offset,
        CancellationToken cancellationToken)
    {
        RequireTransaction(context);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, "read")}(@target, @offset)::text";
        command.Parameters.Add(new NpgsqlParameter("target", NpgsqlDbType.Jsonb)
        { Value = JsonSerializer.Serialize(target) });
        command.Parameters.Add(new NpgsqlParameter("offset", NpgsqlDbType.Bigint) { Value = offset });
        var result = new List<SourceEvidence>(256);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(JsonSerializer.Deserialize<SourceEvidence>(reader.GetString(0))
                ?? throw new InvalidOperationException("The moderation evidence projection was null."));
        return result;
    }

    internal static async Task WriteAsync(
        ExploreDbContext context, AtprotoModerationTarget target, AtprotoModerationDeleteWrite write,
        CancellationToken cancellationToken)
    {
        RequireTransaction(context);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, "write")}(@target, @write)";
        command.Parameters.Add(new NpgsqlParameter("target", NpgsqlDbType.Jsonb)
        { Value = JsonSerializer.Serialize(target) });
        command.Parameters.Add(new NpgsqlParameter("write", NpgsqlDbType.Jsonb)
        { Value = JsonSerializer.Serialize(write) });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void RequireTransaction(ExploreDbContext context)
    {
        if (context.Database.CurrentTransaction is not { } transaction ||
            transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Moderation compensation requires an existing Serializable transaction.");
    }

    public static string BuildSql(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string T<T>() => sql.DelimitIdentifier(
            context.Model.FindEntityType(typeof(T))!.GetTableName()!, Schema(context, typeof(T)));
        string C<T>(string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            return sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        }
        string A<T>(string alias, string property) => alias + "." + C<T>(property);
        string Literal<T>(string property, object value) => context.Model.FindEntityType(typeof(T))!
            .FindProperty(property)!.GetRelationalTypeMapping().GenerateSqlLiteral(value);
        string Text(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        string O(string property) => A<PdsSyncOutbox>("o", property);
        string W(string property) => A<AtprotoOutboundRecordOwnership>("w", property);
        string R(string property) => A<AtprotoRecord>("r", property);
        string E(string property) => A<Event>("e", property);
        string Actor(string property) => A<Actor>("a", property);
        string Identity(string property) => A<AtprotoIdentity>("i", property);
        string S(string property) => A<UserAuthenticationToken>("s", property);
        string L(string property) => A<UserExternalLogin>("l", property);
        string pending = Literal<PdsSyncOutbox>(nameof(PdsSyncOutbox.Status), PdsSyncStatus.Pending);
        string processing = Literal<PdsSyncOutbox>(nameof(PdsSyncOutbox.Status), PdsSyncStatus.Processing);
        string completed = Literal<PdsSyncOutbox>(nameof(PdsSyncOutbox.Status), PdsSyncStatus.Completed);
        string failed = Literal<PdsSyncOutbox>(nameof(PdsSyncOutbox.Status), PdsSyncStatus.Failed);
        string deadLettered = Literal<PdsSyncOutbox>(nameof(PdsSyncOutbox.Status), PdsSyncStatus.DeadLettered);
        string superseded = Literal<PdsSyncOutbox>(nameof(PdsSyncOutbox.Status), PdsSyncStatus.Superseded);
        string create = Literal<PdsSyncOutbox>(nameof(PdsSyncOutbox.Operation), PdsSyncOperation.Create);
        string update = Literal<PdsSyncOutbox>(nameof(PdsSyncOutbox.Operation), PdsSyncOperation.Update);
        string delete = Literal<PdsSyncOutbox>(nameof(PdsSyncOutbox.Operation), PdsSyncOperation.Delete);
        string provider = Literal<UserAuthenticationToken>(nameof(UserAuthenticationToken.Provider),
            AuthenticationProviderDeploymentOptions.AtprotoProvider);
        string loginProvider = Literal<UserExternalLogin>(nameof(UserExternalLogin.AuthenticationProviderId),
            (int)AuthenticationProviderKind.Atproto);
        string emptyHash = Text(AtprotoModerationCompensationStore.EmptyPayloadHash);
        string eventCollection = Text(AtprotoEventPublicationPlanner.EventCollection);
        string read = FunctionSql(context, "read");
        string snapshot = FunctionSql(context, "snapshot");
        string write = FunctionSql(context, "write");
        string pds = FunctionSql(context, "pds");
        string functionSchema = sql.DelimitIdentifier(Schema(context, typeof(PdsSyncOutbox)));
        string[] keys =
        [
            GovernanceSettingKeys.Federation.AtprotoEventValidationProfile,
            GovernanceSettingKeys.Federation.AtprotoPublishMyEvents
        ];
        string keyList = string.Join(", ", keys.Select(Text));
        string transactionGuard = """
            IF current_setting('transaction_isolation') <> 'serializable' THEN
                RAISE EXCEPTION 'atproto_moderation_serializable_transaction_required' USING ERRCODE = '25000';
            END IF;
            """;
        string targetGuard = $"""
            {transactionGuard}
            IF p_target IS NULL OR jsonb_typeof(p_target) IS DISTINCT FROM 'object'
               OR (p_target->>'ActorId')::uuid IS NULL
               OR (p_target->>'ActorId')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
               OR (p_target->>'SubjectStamp')::uuid IS NULL
               OR (p_target->>'SubjectStamp')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
               OR ((p_target->>'IdentityId') IS NULL) <> ((p_target->>'Did') IS NULL)
               OR (p_target->>'IdentityId')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
               OR (p_target->>'Did') IS NOT NULL AND btrim(p_target->>'Did') = '' THEN
                RAISE EXCEPTION 'atproto_moderation_target_invalid' USING ERRCODE = '22023';
            END IF;
            IF NOT EXISTS (
                SELECT 1 FROM {T<Actor>()} a
                WHERE {Actor(nameof(Domain.Actor.Id))} = (p_target->>'ActorId')::uuid
                  AND NOT {Actor(nameof(Domain.Actor.IsDeleted))}
                  AND ((p_target->>'IdentityId') IS NULL
                      AND {Actor(nameof(Domain.Actor.IsSuspended))}
                      AND {Actor(nameof(Domain.Actor.ConcurrencyStamp))} = (p_target->>'SubjectStamp')::uuid
                    OR (p_target->>'IdentityId') IS NOT NULL AND EXISTS (
                        SELECT 1 FROM {T<AtprotoIdentity>()} i
                        WHERE {Identity(nameof(AtprotoIdentity.Id))} = (p_target->>'IdentityId')::uuid
                          AND {Identity(nameof(AtprotoIdentity.ActorId))} = {Actor(nameof(Domain.Actor.Id))}
                          AND {Identity(nameof(AtprotoIdentity.Did))} = p_target->>'Did'
                          AND NOT {Identity(nameof(AtprotoIdentity.IsDeleted))}
                          AND {Identity(nameof(AtprotoIdentity.IsSuspended))}
                          AND {Identity(nameof(AtprotoIdentity.ConcurrencyStamp))} = (p_target->>'SubjectStamp')::uuid))) THEN
                RAISE EXCEPTION 'atproto_moderation_subject_changed' USING ERRCODE = '40001';
            END IF;
            """;
        string outboxLineage = $"""
            {O(nameof(PdsSyncOutbox.TenantId))} = (p_lineage->>'TenantId')::uuid
            AND {O(nameof(PdsSyncOutbox.SourceEntityType))} = p_lineage->>'SourceEntityType'
            AND {O(nameof(PdsSyncOutbox.SourceEntityId))} = (p_lineage->>'SourceEntityId')::uuid
            AND {O(nameof(PdsSyncOutbox.UserId))} = (p_lineage->>'UserId')::uuid
            AND {O(nameof(PdsSyncOutbox.Did))} = p_lineage->>'Did'
            AND {O(nameof(PdsSyncOutbox.Collection))} = p_lineage->>'Collection'
            AND {O(nameof(PdsSyncOutbox.RecordKey))} = p_lineage->>'RecordKey'
            """;
        string settledLineage = $"""
            {W(nameof(AtprotoOutboundRecordOwnership.TenantId))} = (p_lineage->>'TenantId')::uuid
            AND {W(nameof(AtprotoOutboundRecordOwnership.SourceEntityType))} = p_lineage->>'SourceEntityType'
            AND {W(nameof(AtprotoOutboundRecordOwnership.SourceEntityId))} = (p_lineage->>'SourceEntityId')::uuid
            AND {W(nameof(AtprotoOutboundRecordOwnership.UserId))} = (p_lineage->>'UserId')::uuid
            AND {R(nameof(AtprotoRecord.Did))} = p_lineage->>'Did'
            AND {R(nameof(AtprotoRecord.Collection))} = p_lineage->>'Collection'
            AND {R(nameof(AtprotoRecord.RecordKey))} = p_lineage->>'RecordKey'
            AND {R(nameof(AtprotoRecord.TombstonedAt))} IS NULL AND {R(nameof(AtprotoRecord.Cid))} IS NOT NULL
            """;
        string active = $"""
            {O(nameof(PdsSyncOutbox.Status))} IN ({pending}, {processing})
            AND {O(nameof(PdsSyncOutbox.SupersededAt))} IS NULL
            AND {O(nameof(PdsSyncOutbox.SupersededById))} IS NULL
            """;
        string Utc(string column) =>
            $"to_char({column} AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US\"Z\"')";
        string Policy(string key) => $"""
            jsonb_build_object(
                'System', (SELECT jsonb_build_object(
                    'Value', {A<SystemSetting>("ss", nameof(SystemSetting.Value))},
                    'ValueType', {A<SystemSetting>("ss", nameof(SystemSetting.SettingValueTypeId))},
                    'IsLocked', {A<SystemSetting>("ss", nameof(SystemSetting.IsLocked))},
                    'Description', {A<SystemSetting>("ss", nameof(SystemSetting.Description))},
                    'Category', {A<SystemSetting>("ss", nameof(SystemSetting.Category))},
                    'AllowedValues', {A<SystemSetting>("ss", nameof(SystemSetting.AllowedValues))}::text)
                    FROM {T<SystemSetting>()} ss
                    WHERE {A<SystemSetting>("ss", nameof(SystemSetting.SettingKey))} = {Text(key)}),
                'Tenant', (SELECT jsonb_build_object(
                    'Value', {A<TenantSetting>("ts", nameof(TenantSetting.Value))},
                    'IsLocked', {A<TenantSetting>("ts", nameof(TenantSetting.IsLocked))})
                    FROM {T<TenantSetting>()} ts
                    WHERE {A<TenantSetting>("ts", nameof(TenantSetting.TenantId))} = (p_lineage->>'TenantId')::uuid
                      AND {A<TenantSetting>("ts", nameof(TenantSetting.SettingKey))} = {Text(key)}),
                'User', (SELECT {A<UserPreference>("up", nameof(UserPreference.Value))}
                    FROM {T<UserPreference>()} up
                    WHERE {A<UserPreference>("up", nameof(UserPreference.TenantId))} = (p_lineage->>'TenantId')::uuid
                      AND {A<UserPreference>("up", nameof(UserPreference.UserId))} = (p_lineage->>'UserId')::uuid
                      AND {A<UserPreference>("up", nameof(UserPreference.SettingKey))} = {Text(key)}),
                'Organization', NULL, 'Group', NULL, 'HasTenantContext', true, 'SmtpDelegationValue', NULL)
            """;

        (Type Type, string[] Columns, string Predicate)[] reads =
        [
            (typeof(Actor), [nameof(Domain.Actor.Id), nameof(Domain.Actor.IsDeleted),
                nameof(Domain.Actor.IsSuspended), nameof(Domain.Actor.ConcurrencyStamp)], "true"),
            (typeof(AtprotoIdentity), [nameof(AtprotoIdentity.Id), nameof(AtprotoIdentity.ActorId),
                nameof(AtprotoIdentity.Did), nameof(AtprotoIdentity.IsDeleted), nameof(AtprotoIdentity.IsSuspended),
                nameof(AtprotoIdentity.ConcurrencyStamp)], "true"),
            (typeof(Event), [nameof(Event.Id), nameof(Event.TenantId), nameof(Event.ActorId),
                nameof(Event.ConcurrencyStamp)], "true"),
            (typeof(AtprotoOutboundRecordOwnership), [nameof(AtprotoOutboundRecordOwnership.AtprotoRecordId),
                nameof(AtprotoOutboundRecordOwnership.TenantId), nameof(AtprotoOutboundRecordOwnership.UserId),
                nameof(AtprotoOutboundRecordOwnership.SourceEntityType), nameof(AtprotoOutboundRecordOwnership.SourceEntityId)],
                $"{C<AtprotoOutboundRecordOwnership>(nameof(AtprotoOutboundRecordOwnership.SourceEntityType))} = 'Event'"),
            (typeof(AtprotoRecord), [nameof(AtprotoRecord.Id), nameof(AtprotoRecord.Did),
                nameof(AtprotoRecord.Collection), nameof(AtprotoRecord.RecordKey), nameof(AtprotoRecord.Cid),
                nameof(AtprotoRecord.TombstonedAt)], "true"),
            (typeof(PdsSyncOutbox), [nameof(PdsSyncOutbox.Id), nameof(PdsSyncOutbox.TenantId),
                nameof(PdsSyncOutbox.SourceEntityType), nameof(PdsSyncOutbox.SourceEntityId),
                nameof(PdsSyncOutbox.UserId), nameof(PdsSyncOutbox.Did), nameof(PdsSyncOutbox.Collection),
                nameof(PdsSyncOutbox.RecordKey), nameof(PdsSyncOutbox.SourceVersion),
                nameof(PdsSyncOutbox.Operation), nameof(PdsSyncOutbox.Status), nameof(PdsSyncOutbox.CreatedAt),
                nameof(PdsSyncOutbox.NextRetryAt), nameof(PdsSyncOutbox.LeaseExpiresAt), nameof(PdsSyncOutbox.LeaseFence),
                nameof(PdsSyncOutbox.PdsHost), nameof(PdsSyncOutbox.ExpectedCid), nameof(PdsSyncOutbox.AtprotoRecordId),
                nameof(PdsSyncOutbox.SupersededAt), nameof(PdsSyncOutbox.SupersededById),
                nameof(PdsSyncOutbox.Payload), nameof(PdsSyncOutbox.PayloadHash), nameof(PdsSyncOutbox.IdempotencyKey),
                nameof(PdsSyncOutbox.DependsOnAtprotoRecordId), nameof(PdsSyncOutbox.DependsOnCid),
                nameof(PdsSyncOutbox.MaxRetries)],
                $"{C<PdsSyncOutbox>(nameof(PdsSyncOutbox.SourceEntityType))} = 'Event'"),
            (typeof(SystemSetting), [nameof(SystemSetting.SettingKey), nameof(SystemSetting.Value),
                nameof(SystemSetting.SettingValueTypeId), nameof(SystemSetting.IsLocked), nameof(SystemSetting.Description),
                nameof(SystemSetting.Category), nameof(SystemSetting.AllowedValues)],
                $"{C<SystemSetting>(nameof(SystemSetting.SettingKey))} IN ({keyList})"),
            (typeof(TenantSetting), [nameof(TenantSetting.TenantId), nameof(TenantSetting.SettingKey),
                nameof(TenantSetting.Value), nameof(TenantSetting.IsLocked)],
                $"{C<TenantSetting>(nameof(TenantSetting.SettingKey))} IN ({keyList})"),
            (typeof(UserPreference), [nameof(UserPreference.TenantId), nameof(UserPreference.UserId),
                nameof(UserPreference.SettingKey), nameof(UserPreference.Value)],
                $"{C<UserPreference>(nameof(UserPreference.SettingKey))} IN ({keyList})"),
            (typeof(UserAuthenticationToken), [nameof(UserAuthenticationToken.Id), nameof(UserAuthenticationToken.TenantId),
                nameof(UserAuthenticationToken.UserId), nameof(UserAuthenticationToken.Provider),
                nameof(UserAuthenticationToken.SubjectDid), nameof(UserAuthenticationToken.PdsHost)],
                $"{C<UserAuthenticationToken>(nameof(UserAuthenticationToken.Provider))} = {provider}"),
            (typeof(UserExternalLogin), [nameof(UserExternalLogin.UserId), nameof(UserExternalLogin.AuthenticationProviderId),
                nameof(UserExternalLogin.ProviderKey)],
                $"{C<UserExternalLogin>(nameof(UserExternalLogin.AuthenticationProviderId))} = {loginProvider}")
        ];
        string Table(Type type) => sql.DelimitIdentifier(
            context.Model.FindEntityType(type)!.GetTableName()!, Schema(context, type));
        string Columns(Type type, IEnumerable<string> properties)
        {
            var entity = context.Model.FindEntityType(type)!;
            var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return string.Join(", ", properties.Select(property =>
                sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(mapping)!)).Distinct());
        }
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .Select(table => table.TableName).ToHashSet(StringComparer.Ordinal);
        string grants = string.Join(Environment.NewLine, reads.Select(relation =>
        {
            var entity = context.Model.FindEntityType(relation.Type)!;
            string table = Table(relation.Type);
            string allColumns = Columns(relation.Type, entity.GetProperties().Select(property => property.Name));
            string policy = tenantTables.Contains(entity.GetTableName()!) ? $"""
                DROP POLICY IF EXISTS "{PolicyName}_read" ON {table};
                CREATE POLICY "{PolicyName}_read" ON {table}
                    FOR SELECT TO {OwnerRole} USING ({relation.Predicate});
                """ : "";
            return $"""
                REVOKE ALL ON TABLE {table} FROM {OwnerRole}, {GlobalRole};
                REVOKE ALL ({allColumns}) ON TABLE {table} FROM {OwnerRole}, {GlobalRole};
                GRANT SELECT ({Columns(relation.Type, relation.Columns)}) ON TABLE {table} TO {OwnerRole};
                {policy}
                """;
        }));
        string ownerSchemas = string.Join(", ", reads.Select(relation => Schema(context, relation.Type))
            .Distinct(StringComparer.Ordinal).Select(sql.DelimitIdentifier));
        string relationIds = string.Join(", ", reads.Select(relation => Text(Table(relation.Type)) + "::regclass"));
        string[] insertProperties =
        [
            nameof(PdsSyncOutbox.Id), nameof(PdsSyncOutbox.TenantId), nameof(PdsSyncOutbox.UserId),
            nameof(PdsSyncOutbox.Did), nameof(PdsSyncOutbox.Collection), nameof(PdsSyncOutbox.RecordKey),
            nameof(PdsSyncOutbox.Operation), nameof(PdsSyncOutbox.Payload), nameof(PdsSyncOutbox.PayloadHash),
            nameof(PdsSyncOutbox.IdempotencyKey), nameof(PdsSyncOutbox.PdsHost), nameof(PdsSyncOutbox.SourceEntityType),
            nameof(PdsSyncOutbox.SourceEntityId), nameof(PdsSyncOutbox.SourceVersion), nameof(PdsSyncOutbox.AtprotoRecordId),
            nameof(PdsSyncOutbox.ExpectedCid), nameof(PdsSyncOutbox.Status), nameof(PdsSyncOutbox.CreatedAt),
            nameof(PdsSyncOutbox.NextRetryAt), nameof(PdsSyncOutbox.MaxRetries),
            nameof(PdsSyncOutbox.RetryCount), nameof(PdsSyncOutbox.LeaseFence)
        ];
        string[] supersessionProperties =
        [
            nameof(PdsSyncOutbox.Status), nameof(PdsSyncOutbox.SupersededById), nameof(PdsSyncOutbox.SupersededAt),
            nameof(PdsSyncOutbox.LeaseOwner), nameof(PdsSyncOutbox.LeaseToken), nameof(PdsSyncOutbox.LeaseExpiresAt)
        ];
        string insertColumns = Columns(typeof(PdsSyncOutbox), insertProperties);
        string successorMatch = $"""
            {outboxLineage}
            AND {O(nameof(PdsSyncOutbox.Id))} = (p_write->>'SuccessorId')::uuid
            AND {O(nameof(PdsSyncOutbox.SourceVersion))} = (p_write->'Fence'->>'SourceVersion')::uuid
            AND {O(nameof(PdsSyncOutbox.Operation))} = {delete}
            AND {O(nameof(PdsSyncOutbox.Payload))} IS NULL
            AND {O(nameof(PdsSyncOutbox.PayloadHash))} = {emptyHash}
            AND {O(nameof(PdsSyncOutbox.IdempotencyKey))} = replace((p_write->>'SuccessorId')::uuid::text, '-', '')
            AND {O(nameof(PdsSyncOutbox.PdsHost))} = p_write->>'PdsHost'
            AND {O(nameof(PdsSyncOutbox.AtprotoRecordId))} IS NOT DISTINCT FROM (p_write->>'AtprotoRecordId')::uuid
            AND {O(nameof(PdsSyncOutbox.ExpectedCid))} IS NOT DISTINCT FROM p_write->>'ExpectedCid'
            AND {O(nameof(PdsSyncOutbox.DependsOnAtprotoRecordId))} IS NULL
            AND {O(nameof(PdsSyncOutbox.DependsOnCid))} IS NULL
            AND {O(nameof(PdsSyncOutbox.CreatedAt))} = v_created
            AND {O(nameof(PdsSyncOutbox.NextRetryAt))} IS NOT DISTINCT FROM (p_write->>'NotBefore')::timestamptz
            AND {O(nameof(PdsSyncOutbox.MaxRetries))} = 10
            """;
        return $"""
            DO $roles$
            DECLARE v_role text;
            BEGIN
                FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{GlobalRole}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role
                        AND (rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication OR rolinherit)) THEN
                        RAISE EXCEPTION 'atproto_moderation_authority_not_ready';
                    END IF;
                END LOOP;
                IF EXISTS (
                    SELECT 1 FROM pg_catalog.pg_auth_members edge
                    JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                    JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                    WHERE member.rolname IN ('{OwnerRole}', '{GlobalRole}')
                       OR member.rolname = '{MigratorRole}' AND parent.rolname <> '{OwnerRole}'
                       OR parent.rolname = '{OwnerRole}' AND member.rolname <> '{MigratorRole}'
                       OR parent.rolname = '{MigratorRole}' AND member.rolname <> CURRENT_USER) THEN
                    RAISE EXCEPTION 'atproto_moderation_authority_not_ready';
                END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_class
                    WHERE oid IN ({relationIds})
                      AND relowner IN (SELECT oid FROM pg_catalog.pg_roles
                          WHERE rolname IN ('{OwnerRole}', '{GlobalRole}'))) THEN
                    RAISE EXCEPTION 'atproto_moderation_authority_not_ready';
                END IF;
                IF EXISTS (
                    WITH RECURSIVE inherited_roles(roleid) AS (
                        SELECT roleid FROM pg_catalog.pg_auth_members
                        WHERE member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                        UNION
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                        JOIN inherited_roles inherited ON edge.member = inherited.roleid)
                    SELECT 1 FROM inherited_roles
                    WHERE roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}')) THEN
                    RAISE EXCEPTION 'atproto_moderation_authority_not_ready';
                END IF;
            END; $roles$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE ON SCHEMA {ownerSchemas} TO {OwnerRole};
            GRANT USAGE, CREATE ON SCHEMA {functionSchema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {functionSchema} TO {GlobalRole};
            {grants}
            GRANT INSERT ({insertColumns}) ON TABLE {T<PdsSyncOutbox>()} TO {OwnerRole};
            GRANT UPDATE ({Columns(typeof(PdsSyncOutbox), supersessionProperties)})
                ON TABLE {T<PdsSyncOutbox>()} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}_insert" ON {T<PdsSyncOutbox>()};
            CREATE POLICY "{PolicyName}_insert" ON {T<PdsSyncOutbox>()}
                FOR INSERT TO {OwnerRole} WITH CHECK (
                    {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.SourceEntityType))} = 'Event'
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.Operation))} = {delete}
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.Status))} = {failed}
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.Payload))} IS NULL
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.PayloadHash))} = {emptyHash}
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.MaxRetries))} = 10
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.DependsOnAtprotoRecordId))} IS NULL
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.DependsOnCid))} IS NULL);
            DROP POLICY IF EXISTS "{PolicyName}_supersede" ON {T<PdsSyncOutbox>()};
            CREATE POLICY "{PolicyName}_supersede" ON {T<PdsSyncOutbox>()}
                FOR UPDATE TO {OwnerRole}
                USING ({C<PdsSyncOutbox>(nameof(PdsSyncOutbox.SourceEntityType))} = 'Event'
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.Status))} NOT IN ({completed}, {superseded})
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.SupersededAt))} IS NULL
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.SupersededById))} IS NULL)
                WITH CHECK ({C<PdsSyncOutbox>(nameof(PdsSyncOutbox.SourceEntityType))} = 'Event'
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.Status))} = {superseded}
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.SupersededAt))} IS NOT NULL
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.SupersededById))} IS NOT NULL);
            DROP POLICY IF EXISTS "{PolicyName}_activate" ON {T<PdsSyncOutbox>()};
            CREATE POLICY "{PolicyName}_activate" ON {T<PdsSyncOutbox>()}
                FOR UPDATE TO {OwnerRole}
                USING ({C<PdsSyncOutbox>(nameof(PdsSyncOutbox.SourceEntityType))} = 'Event'
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.Operation))} = {delete}
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.Status))} = {failed}
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.Payload))} IS NULL
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.PayloadHash))} = {emptyHash}
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.MaxRetries))} = 10
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.DependsOnAtprotoRecordId))} IS NULL
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.DependsOnCid))} IS NULL
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.SupersededAt))} IS NULL
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.SupersededById))} IS NULL
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.LeaseOwner))} IS NULL
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.LeaseToken))} IS NULL
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.LeaseExpiresAt))} IS NULL)
                WITH CHECK ({C<PdsSyncOutbox>(nameof(PdsSyncOutbox.SourceEntityType))} = 'Event'
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.Operation))} = {delete}
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.Status))} = {pending}
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.Payload))} IS NULL
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.PayloadHash))} = {emptyHash}
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.MaxRetries))} = 10
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.DependsOnAtprotoRecordId))} IS NULL
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.DependsOnCid))} IS NULL
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.SupersededAt))} IS NULL
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.SupersededById))} IS NULL
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.LeaseOwner))} IS NULL
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.LeaseToken))} IS NULL
                    AND {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.LeaseExpiresAt))} IS NULL);
            DO $install$
            DECLARE v_previous text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$
                CREATE OR REPLACE FUNCTION {pds}(p_value text)
                RETURNS text LANGUAGE plpgsql IMMUTABLE STRICT SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                DECLARE v_parts text[]; v_port integer;
                BEGIN
                    v_parts := regexp_match(p_value, '^https://([^/?#@:]+|\[[^]]+\])(?::([0-9]+))?([^?#]*)$', 'i');
                    IF v_parts IS NULL THEN RETURN NULL; END IF;
                    v_port := COALESCE(v_parts[2], '443')::integer;
                    IF v_port < 1 OR v_port > 65535 THEN RETURN NULL; END IF;
                    RETURN lower(v_parts[1]) || ':' || v_port::text || rtrim(COALESCE(v_parts[3], ''), '/');
                END; $body$;

                CREATE OR REPLACE FUNCTION {snapshot}(p_target jsonb, p_lineage jsonb)
                RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                DECLARE v_result jsonb;
                BEGIN
                    {targetGuard}
                    IF p_lineage IS NULL OR jsonb_typeof(p_lineage) IS DISTINCT FROM 'object'
                       OR p_lineage->>'SourceEntityType' IS DISTINCT FROM 'Event'
                       OR (p_lineage->>'TenantId')::uuid IS NULL
                       OR (p_lineage->>'TenantId')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                       OR (p_lineage->>'SourceEntityId')::uuid IS NULL
                       OR (p_lineage->>'SourceEntityId')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                       OR (p_lineage->>'UserId')::uuid IS NULL
                       OR (p_lineage->>'UserId')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                       OR COALESCE(btrim(p_lineage->>'Did'), '') = ''
                       OR p_lineage->>'Collection' IS DISTINCT FROM {eventCollection}
                       OR COALESCE(btrim(p_lineage->>'RecordKey'), '') = ''
                       OR (p_target->>'Did') IS NOT NULL
                           AND (p_lineage->>'Did') IS DISTINCT FROM (p_target->>'Did') THEN
                        RAISE EXCEPTION 'atproto_moderation_lineage_invalid' USING ERRCODE = '22023';
                    END IF;
                    SELECT jsonb_build_object(
                        'Lineage', p_lineage,
                        'Fence', jsonb_build_object('SourceVersion', {E(nameof(Event.ConcurrencyStamp))},
                            'ActorStamp', {Actor(nameof(Domain.Actor.ConcurrencyStamp))},
                            'IdentityStamp', (SELECT {Identity(nameof(AtprotoIdentity.ConcurrencyStamp))}
                                FROM {T<AtprotoIdentity>()} i
                                WHERE {Identity(nameof(AtprotoIdentity.ActorId))} = {Actor(nameof(Domain.Actor.Id))}
                                  AND {Identity(nameof(AtprotoIdentity.Did))} = p_lineage->>'Did'
                                  AND NOT {Identity(nameof(AtprotoIdentity.IsDeleted))}
                                  AND ((p_target->>'IdentityId') IS NULL
                                      OR {Identity(nameof(AtprotoIdentity.Id))} = (p_target->>'IdentityId')::uuid))),
                        'Settled', (SELECT jsonb_build_object(
                                'RecordId', {R(nameof(AtprotoRecord.Id))}, 'Cid', {R(nameof(AtprotoRecord.Cid))})
                            FROM {T<AtprotoOutboundRecordOwnership>()} w
                            JOIN {T<AtprotoRecord>()} r
                              ON {R(nameof(AtprotoRecord.Id))} = {W(nameof(AtprotoOutboundRecordOwnership.AtprotoRecordId))}
                            WHERE {settledLineage}),
                        'Unsettled', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                                'Id', {O(nameof(PdsSyncOutbox.Id))},
                                'Operation', CASE {O(nameof(PdsSyncOutbox.Operation))}
                                    WHEN {create} THEN 1 WHEN {update} THEN 2 WHEN {delete} THEN 3 END,
                                'Status', CASE {O(nameof(PdsSyncOutbox.Status))}
                                    WHEN {pending} THEN 1 WHEN {processing} THEN 2
                                    WHEN {completed} THEN 3 WHEN {failed} THEN 4
                                    WHEN {deadLettered} THEN 5 WHEN {superseded} THEN 6 END,
                                'CreatedAt', {Utc(O(nameof(PdsSyncOutbox.CreatedAt)))},
                                'NextRetryAt', {Utc(O(nameof(PdsSyncOutbox.NextRetryAt)))},
                                'LeaseExpiresAt', {Utc(O(nameof(PdsSyncOutbox.LeaseExpiresAt)))},
                                'LeaseFence', {O(nameof(PdsSyncOutbox.LeaseFence))},
                                'PdsHost', {O(nameof(PdsSyncOutbox.PdsHost))},
                                'ExpectedCid', {O(nameof(PdsSyncOutbox.ExpectedCid))})
                                ORDER BY {O(nameof(PdsSyncOutbox.CreatedAt))}, {O(nameof(PdsSyncOutbox.Id))})
                            FROM {T<PdsSyncOutbox>()} o WHERE {outboxLineage} AND {active}), '[]'::jsonb),
                        'Policy', jsonb_build_object('ValidationProfile', {Policy(keys[0])},
                            'PublishConsent', {Policy(keys[1])}),
                        'Account', (SELECT jsonb_build_object('SessionCount', count(*),
                                'SubjectDid', CASE WHEN count(*) = 1 THEN min(binding.did) END,
                                'PdsHost', CASE WHEN count(*) = 1 THEN min(binding.pds) END,
                                'ExactExternalLoginExists', EXISTS (
                                    SELECT 1 FROM {T<UserExternalLogin>()} l
                                    WHERE {L(nameof(UserExternalLogin.UserId))} = (p_lineage->>'UserId')::uuid
                                      AND {L(nameof(UserExternalLogin.AuthenticationProviderId))} = {loginProvider}
                                      AND {L(nameof(UserExternalLogin.ProviderKey))} = p_lineage->>'Did'))
                            FROM (SELECT {S(nameof(UserAuthenticationToken.SubjectDid))} AS did,
                                    {S(nameof(UserAuthenticationToken.PdsHost))} AS pds
                                FROM {T<UserAuthenticationToken>()} s
                                WHERE {S(nameof(UserAuthenticationToken.TenantId))} = (p_lineage->>'TenantId')::uuid
                                  AND {S(nameof(UserAuthenticationToken.UserId))} = (p_lineage->>'UserId')::uuid
                                  AND {S(nameof(UserAuthenticationToken.Provider))} = {provider}
                                ORDER BY {S(nameof(UserAuthenticationToken.Id))} LIMIT 2) binding))
                        INTO v_result
                    FROM {T<Event>()} e JOIN {T<Actor>()} a
                      ON {Actor(nameof(Domain.Actor.Id))} = {E(nameof(Event.ActorId))}
                    WHERE {E(nameof(Event.TenantId))} = (p_lineage->>'TenantId')::uuid
                      AND {E(nameof(Event.Id))} = (p_lineage->>'SourceEntityId')::uuid
                      AND {Actor(nameof(Domain.Actor.Id))} = (p_target->>'ActorId')::uuid
                      AND NOT {Actor(nameof(Domain.Actor.IsDeleted))};
                    IF v_result IS NULL THEN
                        RAISE EXCEPTION 'atproto_moderation_source_changed' USING ERRCODE = '40001';
                    END IF;
                    RETURN v_result;
                END; $body$;

                CREATE OR REPLACE FUNCTION {read}(p_target jsonb, p_offset bigint)
                RETURNS SETOF jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                BEGIN
                    {targetGuard}
                    IF p_offset IS NULL OR p_offset < 0 THEN
                        RAISE EXCEPTION 'atproto_moderation_offset_invalid' USING ERRCODE = '22023';
                    END IF;
                    RETURN QUERY
                    SELECT {snapshot}(p_target, jsonb_build_object(
                        'TenantId', roots.tenant, 'SourceEntityType', roots.source_type,
                        'SourceEntityId', roots.source_id, 'UserId', roots.user_id,
                        'Did', roots.did, 'Collection', roots.collection, 'RecordKey', roots.record_key))
                    FROM (
                        SELECT {W(nameof(AtprotoOutboundRecordOwnership.TenantId))} AS tenant,
                            {W(nameof(AtprotoOutboundRecordOwnership.SourceEntityType))} AS source_type,
                            {W(nameof(AtprotoOutboundRecordOwnership.SourceEntityId))} AS source_id,
                            {W(nameof(AtprotoOutboundRecordOwnership.UserId))} AS user_id,
                            {R(nameof(AtprotoRecord.Did))} AS did,
                            {R(nameof(AtprotoRecord.Collection))} AS collection,
                            {R(nameof(AtprotoRecord.RecordKey))} AS record_key
                        FROM {T<AtprotoOutboundRecordOwnership>()} w
                        JOIN {T<AtprotoRecord>()} r
                          ON {R(nameof(AtprotoRecord.Id))} = {W(nameof(AtprotoOutboundRecordOwnership.AtprotoRecordId))}
                        JOIN {T<Event>()} e
                          ON {E(nameof(Event.TenantId))} = {W(nameof(AtprotoOutboundRecordOwnership.TenantId))}
                         AND {E(nameof(Event.Id))} = {W(nameof(AtprotoOutboundRecordOwnership.SourceEntityId))}
                        WHERE {W(nameof(AtprotoOutboundRecordOwnership.SourceEntityType))} = 'Event'
                          AND {R(nameof(AtprotoRecord.Collection))} = {eventCollection}
                          AND {E(nameof(Event.ActorId))} = (p_target->>'ActorId')::uuid
                          AND {R(nameof(AtprotoRecord.TombstonedAt))} IS NULL
                          AND {R(nameof(AtprotoRecord.Cid))} IS NOT NULL
                          AND ((p_target->>'Did') IS NULL OR {R(nameof(AtprotoRecord.Did))} = p_target->>'Did')
                        UNION
                        SELECT {O(nameof(PdsSyncOutbox.TenantId))}, {O(nameof(PdsSyncOutbox.SourceEntityType))},
                            {O(nameof(PdsSyncOutbox.SourceEntityId))}, {O(nameof(PdsSyncOutbox.UserId))},
                            {O(nameof(PdsSyncOutbox.Did))}, {O(nameof(PdsSyncOutbox.Collection))},
                            {O(nameof(PdsSyncOutbox.RecordKey))}
                        FROM {T<PdsSyncOutbox>()} o JOIN {T<Event>()} e
                          ON {E(nameof(Event.TenantId))} = {O(nameof(PdsSyncOutbox.TenantId))}
                         AND {E(nameof(Event.Id))} = {O(nameof(PdsSyncOutbox.SourceEntityId))}
                        WHERE {O(nameof(PdsSyncOutbox.SourceEntityType))} = 'Event'
                          AND {O(nameof(PdsSyncOutbox.Collection))} = {eventCollection}
                          AND {E(nameof(Event.ActorId))} = (p_target->>'ActorId')::uuid AND {active}
                          AND ((p_target->>'Did') IS NULL OR {O(nameof(PdsSyncOutbox.Did))} = p_target->>'Did')
                    ) roots
                    ORDER BY roots.tenant, roots.source_type, roots.source_id,
                        roots.user_id, roots.did, roots.collection, roots.record_key
                    LIMIT 256 OFFSET p_offset;
                END; $body$;

                CREATE OR REPLACE FUNCTION {write}(p_target jsonb, p_write jsonb)
                RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                DECLARE p_lineage jsonb; v_snapshot jsonb; v_created timestamptz;
                    v_not_before timestamptz; v_account jsonb; v_pds text; v_activated integer;
                BEGIN
                    {targetGuard}
                    IF p_write IS NULL OR jsonb_typeof(p_write) IS DISTINCT FROM 'object'
                       OR (p_write->>'SuccessorId')::uuid IS NULL
                       OR (p_write->>'SuccessorId')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                       OR COALESCE(p_write->>'CreatedAtUtc', '') !~ '(Z|[+]00:00)$'
                       OR (p_write->>'NotBefore') IS NOT NULL AND (p_write->>'NotBefore') !~ '(Z|[+]00:00)$' THEN
                        RAISE EXCEPTION 'atproto_moderation_write_invalid' USING ERRCODE = '22023';
                    END IF;
                    p_lineage := p_write->'Lineage';
                    v_created := (p_write->>'CreatedAtUtc')::timestamptz;
                    v_snapshot := {snapshot}(p_target, p_lineage);
                    IF v_snapshot->'Fence' IS DISTINCT FROM p_write->'Fence' THEN
                        RAISE EXCEPTION 'atproto_moderation_fence_changed' USING ERRCODE = '40001';
                    END IF;
                    IF EXISTS (SELECT 1 FROM {T<PdsSyncOutbox>()} o
                        WHERE {O(nameof(PdsSyncOutbox.Id))} = (p_write->>'SuccessorId')::uuid) THEN
                        IF NOT EXISTS (SELECT 1 FROM {T<PdsSyncOutbox>()} o WHERE {successorMatch}) THEN
                            RAISE EXCEPTION 'atproto_moderation_successor_conflict' USING ERRCODE = '23505';
                        END IF;
                        RETURN;
                    END IF;
                    IF (v_snapshot->'Settled'->>'RecordId')::uuid IS DISTINCT FROM (p_write->>'AtprotoRecordId')::uuid
                       OR (CASE WHEN jsonb_array_length(v_snapshot->'Unsettled') = 0
                            THEN v_snapshot->'Settled'->>'Cid' END) IS DISTINCT FROM p_write->>'ExpectedCid'
                       OR jsonb_array_length(v_snapshot->'Unsettled') = 0
                          AND COALESCE(btrim(v_snapshot->'Settled'->>'Cid'), '') = '' THEN
                        RAISE EXCEPTION 'atproto_moderation_evidence_changed' USING ERRCODE = '40001';
                    END IF;
                    v_account := v_snapshot->'Account';
                    v_pds := {pds}(p_write->>'PdsHost');
                    IF (v_account->>'SessionCount')::integer IS DISTINCT FROM 1
                       OR v_account->>'SubjectDid' IS DISTINCT FROM p_lineage->>'Did'
                       OR (v_account->>'ExactExternalLoginExists')::boolean IS DISTINCT FROM true
                       OR v_pds IS NULL OR v_pds IS DISTINCT FROM {pds}(v_account->>'PdsHost')
                       OR EXISTS (SELECT 1 FROM jsonb_array_elements(v_snapshot->'Unsettled') mutation
                            WHERE {pds}(mutation->>'PdsHost') IS DISTINCT FROM v_pds) THEN
                        RAISE EXCEPTION 'atproto_moderation_account_binding_changed' USING ERRCODE = '40001';
                    END IF;
                    SELECT max(greatest(
                        CASE WHEN (mutation->>'NextRetryAt')::timestamptz > v_created
                            THEN (mutation->>'NextRetryAt')::timestamptz END,
                        CASE WHEN (mutation->>'Status')::integer = 2
                            THEN greatest(COALESCE((mutation->>'LeaseExpiresAt')::timestamptz,
                                v_created + interval '60 seconds'), v_created + interval '60 seconds')
                                + interval '5 seconds' END))
                        INTO v_not_before FROM jsonb_array_elements(v_snapshot->'Unsettled') mutation;
                    IF v_not_before IS DISTINCT FROM (p_write->>'NotBefore')::timestamptz THEN
                        RAISE EXCEPTION 'atproto_moderation_predecessor_delay_changed' USING ERRCODE = '40001';
                    END IF;
                    INSERT INTO {T<PdsSyncOutbox>()} ({insertColumns})
                        VALUES ((p_write->>'SuccessorId')::uuid, (p_lineage->>'TenantId')::uuid,
                            (p_lineage->>'UserId')::uuid, p_lineage->>'Did', p_lineage->>'Collection',
                            p_lineage->>'RecordKey', {delete}, NULL, {emptyHash},
                            replace((p_write->>'SuccessorId')::uuid::text, '-', ''), p_write->>'PdsHost',
                            'Event', (p_lineage->>'SourceEntityId')::uuid,
                            (p_write->'Fence'->>'SourceVersion')::uuid, (p_write->>'AtprotoRecordId')::uuid,
                            p_write->>'ExpectedCid', {failed}, v_created, (p_write->>'NotBefore')::timestamptz,
                            10, 0, 0);
                    UPDATE {T<PdsSyncOutbox>()} o SET
                        {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.Status))} = {superseded},
                        {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.SupersededById))} = (p_write->>'SuccessorId')::uuid,
                        {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.SupersededAt))} = v_created,
                        {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.LeaseOwner))} = NULL,
                        {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.LeaseToken))} = NULL,
                        {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.LeaseExpiresAt))} = NULL
                    WHERE {outboxLineage} AND {O(nameof(PdsSyncOutbox.Id))} <> (p_write->>'SuccessorId')::uuid
                      AND {O(nameof(PdsSyncOutbox.Status))} NOT IN ({completed}, {superseded})
                      AND {O(nameof(PdsSyncOutbox.SupersededAt))} IS NULL
                      AND {O(nameof(PdsSyncOutbox.SupersededById))} IS NULL;
                    UPDATE {T<PdsSyncOutbox>()} o SET
                        {C<PdsSyncOutbox>(nameof(PdsSyncOutbox.Status))} = {pending}
                    WHERE {successorMatch} AND {O(nameof(PdsSyncOutbox.Status))} = {failed};
                    GET DIAGNOSTICS v_activated = ROW_COUNT;
                    IF v_activated <> 1 THEN
                        RAISE EXCEPTION 'atproto_moderation_activation_conflict' USING ERRCODE = '40001';
                    END IF;
                END; $body$;
                $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {snapshot}(jsonb,jsonb), {pds}(text),
                        {read}(jsonb,bigint), {write}(jsonb,jsonb) FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {read}(jsonb,bigint), {write}(jsonb,jsonb) TO {GlobalRole};
                $acl$;
                PERFORM set_config('role', v_previous, true);
            END; $install$;
            REVOKE CREATE ON SCHEMA {functionSchema} FROM {OwnerRole};
            DO $closed$
            BEGIN
                IF has_schema_privilege('{OwnerRole}', {Text(Schema(context, typeof(PdsSyncOutbox)))}, 'CREATE') THEN
                    RAISE EXCEPTION 'atproto_moderation_authority_not_ready';
                END IF;
            END; $closed$;
            """;
    }

    private static string Schema(ExploreDbContext context, Type type) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(type)!.GetSchema() ?? "public";
}
