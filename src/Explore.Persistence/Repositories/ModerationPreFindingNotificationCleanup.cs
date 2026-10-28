using System.Data;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>Disposes exact private pointers inside the owning withdrawal, expiry or erasure transaction.</summary>
internal static class ModerationPreFindingNotificationCleanup
{
    internal static async Task StageAsync(ExploreDbContext context, Guid tenantId,
        Guid? custodyId, Guid? noticeId, CancellationToken token)
    {
        if (tenantId == Guid.Empty || (custodyId is null) == (noticeId is null)
            || context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Pre-finding pointer cleanup requires an exact purpose and its native transaction.");
        // Source owners have already qualified their purpose. Explicit tenant predicates
        // cover soft-deleted pointers and the exact erasure owner's cross-tenant inventory.
        var notices = context.Set<ModerationPreFindingNotice>()
            .IgnoreAllFilters(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(row => row.TenantId == tenantId
                && (custodyId != null ? row.CustodyId == custodyId : row.Id == noticeId));
        Guid? after = null;
        while (true)
        {
            var rows = await notices.AsNoTracking().Where(row => after == null || row.Id.CompareTo(after.Value) > 0)
                .OrderBy(row => row.Id).Take(100).Select(row => new { row.Id, row.RecipientHumanId }).ToArrayAsync(token);
            if (rows.Length == 0) break;
            var keys = rows.Select(row => ModerationPreFindingNoticeTemplate.DeduplicationKey(row.Id, row.RecipientHumanId))
                .ToArray();
            var intents = context.NotificationIntents
                .IgnoreAllFilters(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .Where(row => row.TenantId == tenantId && keys.Contains(row.DeduplicationKey));
            var ids = await intents.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
            foreach (var id in ids)
                await RelationalEntityRowFence.AcquireAsync<NotificationIntent>(
                    context, tenantId, row => row.Id, id, token);
            // This closed purpose creates an in-app-only graph, never an SMTP/delegation.
            // Delete dependants before roots without loading any private notice payload.
            await context.NotificationDeliveries
                .IgnoreAllFilters(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .Where(row => row.TenantId == tenantId && ids.Contains(row.NotificationIntentId)).ExecuteDeleteAsync(token);
            await context.Notifications
                .IgnoreAllFilters(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .Where(row => row.TenantId == tenantId && row.NotificationIntentId != null
                    && ids.Contains(row.NotificationIntentId.Value)).ExecuteDeleteAsync(token);
            await intents.ExecuteDeleteAsync(token);
            after = rows[^1].Id;
        }
    }
}
