using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

/// <summary>Current-administrator aggregate reporting, without raw quota or accounting-write rights.</summary>
public static class PostgresPlatformApiKeyUsageReportContract
{
    public const string OwnerRole = "event_platform_key_report_owner";
    public const string MigratorRole = "event_platform_key_report_migrator";
    public const string ExecutorRole = "event_platform_key_report_executor";
    internal const string ReportFunction = "platform_api_key_usage_report";

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
        var function = Function(context);
        return sql.Roles(OwnerRole, MigratorRole, ExecutorRole)
            + sql.ActorGrants(OwnerRole, ExecutorRole)
            + sql.Grants<ExternalApiKey>(OwnerRole, ExecutorRole,
                [nameof(ExternalApiKey.Id), nameof(ExternalApiKey.Name), nameof(ExternalApiKey.TenantId),
                 nameof(ExternalApiKey.ExternalApiKeyOwnerTypeId), nameof(ExternalApiKey.OwnerId), nameof(ExternalApiKey.CreditLimit)], [])
            + sql.Grants<ExternalApiKeyQuota>(OwnerRole, ExecutorRole,
                [nameof(ExternalApiKeyQuota.ExternalApiKeyId), nameof(ExternalApiKeyQuota.PeriodStart),
                 nameof(ExternalApiKeyQuota.PeriodEnd), nameof(ExternalApiKeyQuota.RequestCount),
                 nameof(ExternalApiKeyQuota.CreditsUsed)], [])
            + sql.Define(function.Name, function.Arguments, function.Returns, function.Body, function.Owner, function.Executor)
            + sql.Finish(OwnerRole);
    }

    internal static InstanceApiKeySqlFunction Function(ExploreDbContext context)
    {
        var sql = new InstanceApiKeyCapabilitySql(context);
        string K(string property) => sql.Column<ExternalApiKey>(property);
        string Q(string property) => sql.Column<ExternalApiKeyQuota>(property);
        return new(ReportFunction, InstanceApiKeyCapabilitySql.ActorArguments + ", p_from date, p_to date",
            """
            TABLE ("ApiKeyId" uuid, "ApiKeyName" text, "TenantId" uuid,
                "OwnerType" integer, "OwnerId" uuid, "TotalRequestCount" bigint,
                "TotalCreditsUsed" integer, "CreditLimit" integer)
            """, $$"""
            DECLARE {{InstanceApiKeyCapabilitySql.ActorDeclarations}}
            BEGIN
                {{sql.ActorFence}}
                IF p_from IS NULL OR p_to IS NULL OR p_from > p_to THEN
                    RAISE EXCEPTION 'platform_key_report_range_invalid' USING ERRCODE = '22023';
                END IF;
                RETURN QUERY
                SELECT k.{{K(nameof(ExternalApiKey.Id))}}, k.{{K(nameof(ExternalApiKey.Name))}}::text,
                    k.{{K(nameof(ExternalApiKey.TenantId))}}, k.{{K(nameof(ExternalApiKey.ExternalApiKeyOwnerTypeId))}},
                    k.{{K(nameof(ExternalApiKey.OwnerId))}}, sum(q.{{Q(nameof(ExternalApiKeyQuota.RequestCount))}})::bigint,
                    sum(q.{{Q(nameof(ExternalApiKeyQuota.CreditsUsed))}})::integer,
                    coalesce(k.{{K(nameof(ExternalApiKey.CreditLimit))}}, 0)
                FROM {{sql.Table<ExternalApiKeyQuota>()}} AS q
                JOIN {{sql.Table<ExternalApiKey>()}} AS k
                    ON k.{{K(nameof(ExternalApiKey.Id))}} = q.{{Q(nameof(ExternalApiKeyQuota.ExternalApiKeyId))}}
                WHERE q.{{Q(nameof(ExternalApiKeyQuota.PeriodStart))}} >= p_from
                  AND q.{{Q(nameof(ExternalApiKeyQuota.PeriodEnd))}} <= p_to
                GROUP BY k.{{K(nameof(ExternalApiKey.Id))}}, k.{{K(nameof(ExternalApiKey.Name))}},
                    k.{{K(nameof(ExternalApiKey.TenantId))}}, k.{{K(nameof(ExternalApiKey.ExternalApiKeyOwnerTypeId))}},
                    k.{{K(nameof(ExternalApiKey.OwnerId))}}, k.{{K(nameof(ExternalApiKey.CreditLimit))}}
                ORDER BY k.{{K(nameof(ExternalApiKey.Id))}};
            END
            """, OwnerRole, ExecutorRole);
    }
}
