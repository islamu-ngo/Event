using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class WebhookLocalDeliveryAdmission(IWebhookLocalDeliveryAdmissionDbContextFactory admissionFactory)
    : IWebhookLocalDeliveryAdmission
{
    public async Task<IReadOnlyList<Guid>> GetDueTenantIdsAsync(
        int tenantLimit, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await using var context = await admissionFactory.CreateDbContextAsync(cancellationToken);
        return context.Database.IsNpgsql()
            ? await PostgresWebhookLocalDeliveryAdmissionContract.DiscoverAsync(context, tenantLimit, nowUtc, cancellationToken)
            : await new WebhookLocalTargetRepository(context).GetDueTenantIdsAsync(tenantLimit, nowUtc, cancellationToken);
    }

    public async Task<int> CountDueAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await using var context = await admissionFactory.CreateDbContextAsync(cancellationToken);
        return context.Database.IsNpgsql()
            ? await PostgresWebhookLocalDeliveryAdmissionContract.CountAsync(context, false, nowUtc, cancellationToken)
            : await new WebhookLocalTargetRepository(context).CountDueAsync(nowUtc, cancellationToken);
    }

    public async Task<int> CountStaleDeliveringAsync(DateTimeOffset observedAtUtc, CancellationToken cancellationToken)
    {
        await using var context = await admissionFactory.CreateDbContextAsync(cancellationToken);
        return context.Database.IsNpgsql()
            ? await PostgresWebhookLocalDeliveryAdmissionContract.CountAsync(context, true, observedAtUtc, cancellationToken)
            : await new WebhookLocalTargetRepository(context).CountStaleDeliveringAsync(observedAtUtc, cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookLocalDeliveryClaim>> ClaimDueAsync(
        WebhookLocalTargetClaimRequest request, IReadOnlyDictionary<Guid, WebhookDeliveryClaimLimits> tenantLimits,
        CancellationToken cancellationToken)
    {
        await using var context = await admissionFactory.CreateDbContextAsync(cancellationToken);
        if (context.Database.IsNpgsql())
            return await PostgresWebhookLocalDeliveryAdmissionContract.ClaimAsync(context, request, tenantLimits, cancellationToken);

        var claims = await new WebhookLocalTargetRepository(context).ClaimDueAsync(request, tenantLimits, cancellationToken);
        return claims.Select(claim => new WebhookLocalDeliveryClaim(claim.Target.TenantId, claim.Target.Id,
            claim.LeaseToken, claim.DeliveryFence, claim.ClaimedAtUtc, claim.LeaseExpiresAtUtc,
            claim.Target.NextActionAtUtc)).ToArray();
    }

    public async Task<int> RecoverExpiredClaimsAsync(
        DateTimeOffset recoveredAtUtc, string failureCategory, int batchSize, CancellationToken cancellationToken)
    {
        await using var context = await admissionFactory.CreateDbContextAsync(cancellationToken);
        return context.Database.IsNpgsql()
            ? await PostgresWebhookLocalDeliveryAdmissionContract.RecoverAsync(
                context, recoveredAtUtc, failureCategory, batchSize, cancellationToken)
            : await new WebhookLocalTargetRepository(context).RecoverExpiredClaimsAsync(
                recoveredAtUtc, failureCategory, batchSize, cancellationToken);
    }

    public async Task<WebhookDeliverySettingGroup> GetGovernanceSettingsAsync(
        Guid? tenantId, CancellationToken cancellationToken)
    {
        await using var context = await admissionFactory.CreateDbContextAsync(cancellationToken);
        var keys = WebhookDeliverySettingGroup.SettingKeys.ToArray();
        var system = context.Database.IsNpgsql()
            ? await context.SystemSettings.FromSqlRaw(
                $"SELECT * FROM {PostgresWebhookLocalDeliveryAdmissionContract.SystemGovernanceFunctionSql(context)}()")
                .AsNoTracking().ToListAsync(cancellationToken)
            : await context.SystemSettings.AsNoTracking().Where(row => keys.Contains(row.SettingKey))
                .ToListAsync(cancellationToken);
        List<TenantSetting>? tenant = null;
        if (tenantId is { } owner)
        {
            tenant = context.Database.IsNpgsql()
                ? await context.TenantSettingOverrides.FromSqlRaw(
                    $"SELECT * FROM {PostgresWebhookLocalDeliveryAdmissionContract.TenantGovernanceFunctionSql(context)}({{0}})", owner)
                    .IgnoreQueryFilters(["Tenant"]).AsNoTracking().ToListAsync(cancellationToken)
                : await context.TenantSettingOverrides.IgnoreQueryFilters(["Tenant"]).AsNoTracking()
                    .Where(row => row.TenantId == owner && keys.Contains(row.SettingKey)).ToListAsync(cancellationToken);
        }
        var systemMap = system.ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        var tenantMap = tenant?.ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        var resolved = keys.Select(key => HierarchicalSettingMerge.Resolve(key, systemMap, tenantMap))
            .Where(row => row is not null).ToDictionary(row => row!.Key, row => row!, StringComparer.Ordinal);
        var group = new WebhookDeliverySettingGroup();
        group.Populate(resolved);
        return group;
    }
}
