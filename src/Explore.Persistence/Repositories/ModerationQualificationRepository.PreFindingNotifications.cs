using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private async Task RequirePreFindingNotificationGraphAsync(ModerationPreFindingNotice notice, CancellationToken token)
    {
        var key = ModerationPreFindingNoticeTemplate.DeduplicationKey(notice.Id, notice.RecipientHumanId);
        var id = await context.NotificationIntents.AsNoTracking()
            .Where(row => row.TenantId == notice.TenantId && row.DeduplicationKey == key)
            .Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (id is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RelationalEntityRowFence.AcquireAsync<NotificationIntent>(
            context, notice.TenantId, row => row.Id, id.Value, token);
        var graph = await context.NotificationIntents.AsNoTracking()
            .Include(row => row.Deliveries).ThenInclude(row => row.Notification)
            .Include(row => row.ExternalDelegations)
            .SingleOrDefaultAsync(row => row.Id == id && row.TenantId == notice.TenantId, token);
        if (graph is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        foreach (var delivery in graph.Deliveries.OrderBy(row => row.Id))
        {
            await RelationalEntityRowFence.AcquireAsync<NotificationDelivery>(
                context, notice.TenantId, row => row.Id, delivery.Id, token);
            if (delivery.NotificationId is { } notificationId)
                await RelationalEntityRowFence.AcquireAsync<Notification>(
                    context, notice.TenantId, row => row.Id, notificationId, token);
        }
        graph = await context.NotificationIntents.AsNoTracking()
            .Include(row => row.Deliveries).ThenInclude(row => row.Notification)
            .Include(row => row.ExternalDelegations)
            .SingleOrDefaultAsync(row => row.Id == id && row.TenantId == notice.TenantId, token);
        if (graph is null || !ModerationPreFindingNotificationGraph.Matches(graph, notice)
            || await context.EmailDispatchOutbox.AnyAsync(
                row => row.TenantId == notice.TenantId && row.NotificationIntentId == id, token)
            || clock.GetUtcNow() < notice.IssuedAt || clock.GetUtcNow() >= notice.RetainUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }
}
