using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

/// <summary>Separate metadata-read and fixed policy/revocation execution rights.</summary>
public static class PostgresInstanceApiKeyManagementContract
{
    public const string ReadOwner = "event_instance_key_read_owner";
    public const string ReadMigrator = "event_instance_key_read_migrator";
    public const string ReadExecutor = "event_instance_key_read_executor";
    public const string WriteOwner = "event_instance_key_write_owner";
    public const string WriteMigrator = "event_instance_key_write_migrator";
    public const string WriteExecutor = "event_instance_key_write_executor";

    internal const string ReadFunction = "instance_api_key_metadata_read";
    internal const string WriteReadFunction = "instance_api_key_metadata_for_update";
    internal const string NameFunction = "instance_api_key_name_exists";
    internal const string UpdateFunction = "instance_api_key_policy_update";
    internal const string RevokeFunction = "instance_api_key_revoke";

    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context)
    {
        var sql = new InstanceApiKeyCapabilitySql(context);
        string result = sql.Roles(ReadOwner, ReadMigrator, ReadExecutor)
            + sql.ActorGrants(ReadOwner, ReadExecutor)
            + sql.Grants<ExternalApiKey>(ReadOwner, ReadExecutor, sql.MetadataReadColumns, [nameof(ExternalApiKey.Id)])
            + sql.Roles(WriteOwner, WriteMigrator, WriteExecutor)
            + sql.ActorGrants(WriteOwner, WriteExecutor)
            + sql.Grants<ExternalApiKey>(WriteOwner, WriteExecutor, sql.MetadataReadColumns,
                [nameof(ExternalApiKey.Id), nameof(ExternalApiKey.Name), nameof(ExternalApiKey.Scopes),
                 nameof(ExternalApiKey.ExpiresAt), nameof(ExternalApiKey.ExternalApiKeyStatusId),
                 nameof(ExternalApiKey.UpdatedAt), nameof(ExternalApiKey.UpdatedBy)]);
        foreach (var function in Functions(context))
            result += sql.Define(function.Name, function.Arguments, function.Returns,
                function.Body, function.Owner, function.Executor);
        return result + sql.Finish(ReadOwner) + sql.Finish(WriteOwner);
    }

    internal static IReadOnlyList<InstanceApiKeySqlFunction> Functions(ExploreDbContext context)
    {
        var sql = new InstanceApiKeyCapabilitySql(context);
        string C(string property) => sql.Column<ExternalApiKey>(property);
        string table = sql.Table<ExternalApiKey>();
        string platform = $"k.{C(nameof(ExternalApiKey.TenantId))} IS NULL AND k.{C(nameof(ExternalApiKey.ExternalApiKeyOwnerTypeId))} = {(int)ExternalApiKeyOwnerType.InstanceAdmin}";
        string target = $"{platform} AND k.{C(nameof(ExternalApiKey.Id))} = p_key";
        string actorArgs = InstanceApiKeyCapabilitySql.ActorArguments;
        string ownerGate = $$"""
            SELECT k.{{C(nameof(ExternalApiKey.OwnerId))}} INTO v_owner FROM {{table}} AS k WHERE {{target}};
            IF v_owner IS NOT NULL THEN
                PERFORM pg_catalog.pg_advisory_xact_lock(
                    pg_catalog.hashtextextended('instance-api-key-owner:' || v_owner::text, 0));
            END IF;
            {{sql.ActorFence}}
            PERFORM k.{{C(nameof(ExternalApiKey.Id))}} FROM {{table}} AS k
            WHERE {{target}} AND k.{{C(nameof(ExternalApiKey.OwnerId))}} = v_owner FOR UPDATE;
            """;
        string declarations = $"DECLARE {InstanceApiKeyCapabilitySql.ActorDeclarations} v_owner uuid;";
        string allowedScopes = string.Join(", ", ExternalApiKeyScopes.All.Order(StringComparer.Ordinal)
            .Select(scope => $"'{scope}'"));
        return
        [
            new(ReadFunction, actorArgs + ", p_key uuid", $"TABLE ({sql.MetadataColumns})", $$"""
                DECLARE {{InstanceApiKeyCapabilitySql.ActorDeclarations}}
                BEGIN
                    {{sql.ActorFence}}
                    PERFORM k.{{C(nameof(ExternalApiKey.Id))}} FROM {{table}} AS k
                    WHERE {{platform}} AND (p_key IS NOT NULL AND k.{{C(nameof(ExternalApiKey.Id))}} = p_key
                        OR p_key IS NULL AND k.{{C(nameof(ExternalApiKey.OwnerId))}} = p_actor)
                    ORDER BY k.{{C(nameof(ExternalApiKey.Id))}} FOR UPDATE;
                    RETURN QUERY SELECT {{sql.MetadataProjection("k")}} FROM {{table}} AS k
                    WHERE {{platform}} AND (p_key IS NOT NULL AND k.{{C(nameof(ExternalApiKey.Id))}} = p_key
                        OR p_key IS NULL AND k.{{C(nameof(ExternalApiKey.OwnerId))}} = p_actor)
                    ORDER BY k.{{C(nameof(ExternalApiKey.Id))}};
                END
                """, ReadOwner, ReadExecutor),
            new(WriteReadFunction, actorArgs + ", p_key uuid", $"TABLE ({sql.MetadataColumns})", $$"""
                {{declarations}}
                BEGIN
                    {{ownerGate}}
                    RETURN QUERY SELECT {{sql.MetadataProjection("k")}} FROM {{table}} AS k
                    WHERE {{target}} AND k.{{C(nameof(ExternalApiKey.OwnerId))}} = v_owner;
                END
                """, WriteOwner, WriteExecutor),
            new(NameFunction, actorArgs + ", p_key uuid, p_name text", "boolean", $$"""
                {{declarations}}
                BEGIN
                    {{ownerGate}}
                    RETURN EXISTS (SELECT 1 FROM {{table}} AS k
                        WHERE {{platform}} AND k.{{C(nameof(ExternalApiKey.OwnerId))}} = v_owner
                          AND k.{{C(nameof(ExternalApiKey.Name))}} = p_name
                          AND k.{{C(nameof(ExternalApiKey.Id))}} <> p_key);
                END
                """, WriteOwner, WriteExecutor),
            new(UpdateFunction, actorArgs + ", p_key uuid, p_name text, p_scopes text, p_expires timestamp with time zone, p_changed timestamp with time zone",
                "boolean", $$"""
                {{declarations}}
                BEGIN
                    {{ownerGate}}
                    IF p_name IS NULL OR length(btrim(p_name)) NOT BETWEEN 1 AND 200
                       OR p_name ~ '[[:cntrl:]]' OR p_scopes IS NULL OR length(p_scopes) NOT BETWEEN 1 AND 1000
                       OR p_changed IS NULL OR EXISTS (
                           SELECT 1 FROM unnest(string_to_array(p_scopes, ' ')) AS supplied(scope)
                           WHERE lower(supplied.scope) <> ALL(ARRAY[{{allowedScopes}}]::text[])) THEN
                        RAISE EXCEPTION 'instance_key_policy_invalid' USING ERRCODE = '22023';
                    END IF;
                    IF EXISTS (SELECT 1 FROM {{table}} AS k WHERE {{platform}}
                        AND k.{{C(nameof(ExternalApiKey.OwnerId))}} = v_owner
                        AND k.{{C(nameof(ExternalApiKey.Name))}} = btrim(p_name)
                        AND k.{{C(nameof(ExternalApiKey.Id))}} <> p_key) THEN
                        RAISE EXCEPTION 'instance_key_name_conflict' USING ERRCODE = '23505';
                    END IF;
                    UPDATE {{table}} AS k SET {{C(nameof(ExternalApiKey.Name))}} = btrim(p_name),
                        {{C(nameof(ExternalApiKey.Scopes))}} = p_scopes,
                        {{C(nameof(ExternalApiKey.ExpiresAt))}} = p_expires,
                        {{C(nameof(ExternalApiKey.UpdatedAt))}} = p_changed,
                        {{C(nameof(ExternalApiKey.UpdatedBy))}} = p_actor
                    WHERE {{target}} AND k.{{C(nameof(ExternalApiKey.OwnerId))}} = v_owner;
                    RETURN FOUND;
                END
                """, WriteOwner, WriteExecutor),
            new(RevokeFunction, actorArgs + ", p_key uuid, p_changed timestamp with time zone", "boolean", $$"""
                {{declarations}}
                BEGIN
                    {{ownerGate}}
                    IF p_changed IS NULL THEN
                        RAISE EXCEPTION 'instance_key_policy_invalid' USING ERRCODE = '22023';
                    END IF;
                    UPDATE {{table}} AS k SET {{C(nameof(ExternalApiKey.ExternalApiKeyStatusId))}} = {{(int)ExternalApiKeyStatusEnum.Revoked}},
                        {{C(nameof(ExternalApiKey.UpdatedAt))}} = p_changed,
                        {{C(nameof(ExternalApiKey.UpdatedBy))}} = p_actor
                    WHERE {{target}} AND k.{{C(nameof(ExternalApiKey.OwnerId))}} = v_owner
                        AND k.{{C(nameof(ExternalApiKey.ExternalApiKeyStatusId))}} <> {{(int)ExternalApiKeyStatusEnum.Revoked}};
                    RETURN EXISTS (SELECT 1 FROM {{table}} AS k WHERE {{target}}
                        AND k.{{C(nameof(ExternalApiKey.OwnerId))}} = v_owner);
                END
                """, WriteOwner, WriteExecutor)
        ];
    }
}
