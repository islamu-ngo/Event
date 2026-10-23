using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Self-owned insertion and digest-bound, nonreopening metadata recovery.</summary>
public static class PostgresInstanceApiKeyIssuanceContract
{
    public const string OwnerRole = "event_instance_key_issuance_owner";
    public const string MigratorRole = "event_instance_key_issuance_migrator";
    public const string ExecutorRole = "event_instance_key_issuance_executor";
    internal const string AuthorityFunction = "instance_api_key_issuance_authority";
    internal const string NameFunction = "instance_api_key_issuance_name_exists";
    internal const string ReceiptFunction = "instance_api_key_issuance_receipt";
    internal const string RecoveryFunction = "instance_api_key_issuance_recovery";
    internal const string CreateFunction = "instance_api_key_issue";

    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = BuildSql(context);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context)
    {
        var sql = new InstanceApiKeyCapabilitySql(context);
        string[] keyInserts =
        [
            nameof(ExternalApiKey.Id), nameof(ExternalApiKey.TenantId), nameof(ExternalApiKey.Name),
            nameof(ExternalApiKey.Description), nameof(ExternalApiKey.KeyId), nameof(ExternalApiKey.SecretHash),
            nameof(ExternalApiKey.Scopes), nameof(ExternalApiKey.ExternalApiKeyOwnerTypeId), nameof(ExternalApiKey.OwnerId),
            nameof(ExternalApiKey.ExternalApiKeyStatusId), nameof(ExternalApiKey.ExternalApiKeyCreditPeriodId),
            nameof(ExternalApiKey.CreditLimit), nameof(ExternalApiKey.MaxRolloverCredits), nameof(ExternalApiKey.ExpiresAt),
            nameof(ExternalApiKey.CreatedAt), nameof(ExternalApiKey.CreatedBy)
        ];
        string[] receiptColumns =
        [
            nameof(ExternalApiKeyIssuanceReceipt.Id), nameof(ExternalApiKeyIssuanceReceipt.OperationFingerprint),
            nameof(ExternalApiKeyIssuanceReceipt.InputDigest), nameof(ExternalApiKeyIssuanceReceipt.TenantId),
            nameof(ExternalApiKeyIssuanceReceipt.ExternalApiKeyId), nameof(ExternalApiKeyIssuanceReceipt.CreatedAtUtc)
        ];
        string result = sql.Roles(OwnerRole, MigratorRole, ExecutorRole)
            + sql.ActorGrants(OwnerRole, ExecutorRole)
            + sql.Grants<ExternalApiKey>(OwnerRole, ExecutorRole, sql.MetadataReadColumns,
                [nameof(ExternalApiKey.Id)], keyInserts)
            + sql.Grants<ExternalApiKeyIssuanceReceipt>(OwnerRole, ExecutorRole, receiptColumns, [], receiptColumns)
            + sql.Grants<ExternalApiKeyStatus>(OwnerRole, ExecutorRole,
                [nameof(ExternalApiKeyStatus.Id), nameof(ExternalApiKeyStatus.IsUsable)], [nameof(ExternalApiKeyStatus.Id)]);
        foreach (var function in Functions(context))
            result += sql.Define(function.Name, function.Arguments, function.Returns,
                function.Body, function.Owner, function.Executor);
        return result + sql.Finish(OwnerRole);
    }

    internal static IReadOnlyList<InstanceApiKeySqlFunction> Functions(ExploreDbContext context)
    {
        var sql = new InstanceApiKeyCapabilitySql(context);
        string K(string property) => sql.Column<ExternalApiKey>(property);
        string R(string property) => sql.Column<ExternalApiKeyIssuanceReceipt>(property);
        string keys = sql.Table<ExternalApiKey>();
        string receipts = sql.Table<ExternalApiKeyIssuanceReceipt>();
        string args = InstanceApiKeyCapabilitySql.ActorArguments + ", p_operation text";
        string declarations = $"DECLARE {InstanceApiKeyCapabilitySql.ActorDeclarations} v_fingerprint text;";
        string ownKey = $"k.{K(nameof(ExternalApiKey.TenantId))} IS NULL"
            + $" AND k.{K(nameof(ExternalApiKey.ExternalApiKeyOwnerTypeId))} = {(int)ExternalApiKeyOwnerType.InstanceAdmin}"
            + $" AND k.{K(nameof(ExternalApiKey.OwnerId))} = p_actor";
        string gate = $$"""
            IF p_operation IS NULL OR p_operation !~ '^[A-Za-z0-9._:-]{1,128}$' THEN
                RAISE EXCEPTION 'instance_key_operation_invalid' USING ERRCODE = '22023';
            END IF;
            PERFORM pg_catalog.pg_advisory_xact_lock(
                pg_catalog.hashtextextended('instance-api-key-owner:' || p_actor::text, 0));
            {{sql.ActorFence}}
            v_fingerprint := pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(
                'external-api-key-issuance:v1|principal:' || replace(p_actor::text, '-', '')
                || '|scope:global|owner-type:{{(int)ExternalApiKeyOwnerType.InstanceAdmin}}|owner:'
                || replace(p_actor::text, '-', '') || '|operation-key:' || length(p_operation)::text
                || ':' || p_operation, 'UTF8')), 'hex');
            """;
        string allowedScopes = string.Join(", ", ExternalApiKeyScopes.All.Order(StringComparer.Ordinal)
            .Select(scope => $"'{scope}'"));
        return
        [
            new(AuthorityFunction, args, "boolean", $$"""
                {{declarations}}
                BEGIN
                    {{gate}}
                    RETURN true;
                END
                """, OwnerRole, ExecutorRole),
            new(NameFunction, args + ", p_name text", "boolean", $$"""
                {{declarations}}
                BEGIN
                    {{gate}}
                    RETURN EXISTS (SELECT 1 FROM {{keys}} AS k WHERE {{ownKey}}
                        AND k.{{K(nameof(ExternalApiKey.Name))}} = p_name);
                END
                """, OwnerRole, ExecutorRole),
            new(ReceiptFunction, args, $"SETOF {receipts}", $$"""
                {{declarations}}
                BEGIN
                    {{gate}}
                    RETURN QUERY SELECT r.* FROM {{receipts}} AS r
                    WHERE r.{{R(nameof(ExternalApiKeyIssuanceReceipt.TenantId))}} IS NULL
                      AND r.{{R(nameof(ExternalApiKeyIssuanceReceipt.OperationFingerprint))}} = v_fingerprint;
                END
                """, OwnerRole, ExecutorRole),
            new(RecoveryFunction, args + ", p_key uuid", $"TABLE ({sql.MetadataColumns})", $$"""
                {{declarations}}
                BEGIN
                    {{gate}}
                    PERFORM k.{{K(nameof(ExternalApiKey.Id))}} FROM {{keys}} AS k
                    WHERE {{ownKey}} AND k.{{K(nameof(ExternalApiKey.Id))}} = p_key FOR UPDATE;
                    PERFORM s.{{sql.Column<ExternalApiKeyStatus>(nameof(ExternalApiKeyStatus.Id))}}
                    FROM {{sql.Table<ExternalApiKeyStatus>()}} AS s
                    JOIN {{keys}} AS k ON k.{{K(nameof(ExternalApiKey.ExternalApiKeyStatusId))}} = s.{{sql.Column<ExternalApiKeyStatus>(nameof(ExternalApiKeyStatus.Id))}}
                    WHERE {{ownKey}} AND k.{{K(nameof(ExternalApiKey.Id))}} = p_key FOR UPDATE OF s;
                    RETURN QUERY SELECT {{sql.MetadataProjection("k")}} FROM {{keys}} AS k
                    JOIN {{sql.Table<ExternalApiKeyStatus>()}} AS s
                        ON s.{{sql.Column<ExternalApiKeyStatus>(nameof(ExternalApiKeyStatus.Id))}} = k.{{K(nameof(ExternalApiKey.ExternalApiKeyStatusId))}}
                    JOIN {{receipts}} AS r ON r.{{R(nameof(ExternalApiKeyIssuanceReceipt.ExternalApiKeyId))}} = k.{{K(nameof(ExternalApiKey.Id))}}
                    WHERE {{ownKey}} AND k.{{K(nameof(ExternalApiKey.Id))}} = p_key
                      AND r.{{R(nameof(ExternalApiKeyIssuanceReceipt.TenantId))}} IS NULL
                      AND r.{{R(nameof(ExternalApiKeyIssuanceReceipt.OperationFingerprint))}} = v_fingerprint
                      AND s.{{sql.Column<ExternalApiKeyStatus>(nameof(ExternalApiKeyStatus.IsUsable))}}
                      AND (k.{{K(nameof(ExternalApiKey.ExpiresAt))}} IS NULL
                           OR k.{{K(nameof(ExternalApiKey.ExpiresAt))}} > clock_timestamp());
                END
                """, OwnerRole, ExecutorRole),
            new(CreateFunction, args + ", p_key jsonb, p_receipt uuid, p_digest text, p_created timestamp with time zone", "boolean", $$"""
                {{declarations}} v_key record;
                BEGIN
                    {{gate}}
                    SELECT * INTO v_key FROM jsonb_to_record(p_key) AS value(
                        "Id" uuid, "Name" text, "Description" text, "KeyId" text, "SecretHash" text,
                        "Scopes" text, "CreditPeriodId" integer, "CreditLimit" integer,
                        "MaxRolloverCredits" integer, "ExpiresAt" timestamp with time zone);
                    IF v_key."Id" IS NULL OR v_key."Id" = '00000000-0000-0000-0000-000000000000'::uuid
                       OR p_receipt IS NULL OR p_receipt = '00000000-0000-0000-0000-000000000000'::uuid
                       OR p_digest IS NULL OR p_digest !~ '^[0-9a-f]{64}$' OR p_created IS NULL
                       OR v_key."Name" IS NULL OR length(btrim(v_key."Name")) NOT BETWEEN 1 AND 200
                       OR v_key."Name" ~ '[[:cntrl:]]' OR length(v_key."Description") > 1000
                       OR v_key."KeyId" IS NULL OR length(v_key."KeyId") NOT BETWEEN 1 AND 64
                       OR v_key."SecretHash" IS NULL OR length(v_key."SecretHash") NOT BETWEEN 1 AND 500
                       OR v_key."Scopes" IS NULL OR length(v_key."Scopes") NOT BETWEEN 1 AND 1000
                       OR EXISTS (SELECT 1 FROM unnest(string_to_array(v_key."Scopes", ' ')) AS supplied(scope)
                           WHERE lower(supplied.scope) <> ALL(ARRAY[{{allowedScopes}}]::text[])) THEN
                        RAISE EXCEPTION 'instance_key_issuance_invalid' USING ERRCODE = '22023';
                    END IF;
                    IF EXISTS (SELECT 1 FROM {{keys}} AS k WHERE {{ownKey}}
                        AND k.{{K(nameof(ExternalApiKey.Name))}} = btrim(v_key."Name")) THEN
                        RAISE EXCEPTION 'instance_key_name_conflict' USING ERRCODE = '23505';
                    END IF;
                    INSERT INTO {{keys}} (
                        {{K(nameof(ExternalApiKey.Id))}}, {{K(nameof(ExternalApiKey.TenantId))}}, {{K(nameof(ExternalApiKey.Name))}},
                        {{K(nameof(ExternalApiKey.Description))}}, {{K(nameof(ExternalApiKey.KeyId))}}, {{K(nameof(ExternalApiKey.SecretHash))}},
                        {{K(nameof(ExternalApiKey.Scopes))}}, {{K(nameof(ExternalApiKey.ExternalApiKeyOwnerTypeId))}}, {{K(nameof(ExternalApiKey.OwnerId))}},
                        {{K(nameof(ExternalApiKey.ExternalApiKeyStatusId))}}, {{K(nameof(ExternalApiKey.ExternalApiKeyCreditPeriodId))}},
                        {{K(nameof(ExternalApiKey.CreditLimit))}}, {{K(nameof(ExternalApiKey.MaxRolloverCredits))}}, {{K(nameof(ExternalApiKey.ExpiresAt))}},
                        {{K(nameof(ExternalApiKey.CreatedAt))}}, {{K(nameof(ExternalApiKey.CreatedBy))}})
                    VALUES (v_key."Id", NULL, btrim(v_key."Name"), v_key."Description", v_key."KeyId", v_key."SecretHash",
                        v_key."Scopes", {{(int)ExternalApiKeyOwnerType.InstanceAdmin}}, p_actor,
                        {{(int)ExternalApiKeyStatusEnum.Active}}, v_key."CreditPeriodId", v_key."CreditLimit",
                        v_key."MaxRolloverCredits", v_key."ExpiresAt", p_created, p_actor);
                    INSERT INTO {{receipts}} (
                        {{R(nameof(ExternalApiKeyIssuanceReceipt.Id))}}, {{R(nameof(ExternalApiKeyIssuanceReceipt.OperationFingerprint))}},
                        {{R(nameof(ExternalApiKeyIssuanceReceipt.InputDigest))}}, {{R(nameof(ExternalApiKeyIssuanceReceipt.TenantId))}},
                        {{R(nameof(ExternalApiKeyIssuanceReceipt.ExternalApiKeyId))}}, {{R(nameof(ExternalApiKeyIssuanceReceipt.CreatedAtUtc))}})
                    VALUES (p_receipt, v_fingerprint, p_digest, NULL, v_key."Id", p_created);
                    RETURN true;
                END
                """, OwnerRole, ExecutorRole)
        ];
    }
}
