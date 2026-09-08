using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class WebhookMessageRepository(ExploreDbContext dbContext) : IWebhookMessageRepository
{
    public async Task<WebhookMessage> CreateAsync(WebhookMessage message, CancellationToken cancellationToken)
    {
        if (message.Id == Guid.Empty)
        {
            throw new ArgumentException("Webhook messages must be created through the domain factory.", nameof(message));
        }

        await dbContext.WebhookMessages.AddAsync(message, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return message;
    }

    public Task<WebhookMessage?> GetByIdForOwnerOperationAsync(
        Guid messageId,
        CancellationToken cancellationToken) =>
        dbContext.WebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookOwnerOperation)
            .AsNoTracking()
            .Include(message => message.Consumer)
            .FirstOrDefaultAsync(message => message.Id == messageId, cancellationToken);

    public async Task<IReadOnlyList<WebhookMessage>> ListByOwnerAsync(
        WebhookOwnershipScope ownership,
        int limit,
        CancellationToken cancellationToken)
    {
        return await ApplyOwnerPredicate(
                dbContext.WebhookMessages
                    .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookOwnerOperation),
                ownership)
            .AsNoTracking()
            .Include(message => message.Consumer)
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<WebhookMessage?> GetByTenantAndIdAsync(
        Guid tenantId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        return await dbContext.WebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == messageId, cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookMessage>> ListByTenantAsync(
        Guid tenantId,
        int limit,
        CancellationToken cancellationToken)
    {
        return await dbContext.WebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> ClearExpiredPayloadsAsync(
        DateTime now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var messages = await dbContext.WebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookWorkerCrossTenantQueue)
            .Where(e => EF.Property<byte[]?>(e, "_payloadBytes") != null && e.PayloadRetentionUntil <= now)
            .OrderBy(e => e.PayloadRetentionUntil)
            .ThenBy(e => e.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            message.ClearPayload(now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return messages.Count;
    }

    private static IQueryable<WebhookMessage> ApplyOwnerPredicate(
        IQueryable<WebhookMessage> query,
        WebhookOwnershipScope ownership) => ownership.Kind switch
        {
            WebhookConsumerKind.Instance => query.Where(message =>
                message.Consumer != null &&
                message.Consumer.ConsumerKindId == (int)WebhookConsumerKind.Instance &&
                message.Consumer.InstanceId == ownership.InstanceId),
            WebhookConsumerKind.Tenant => query.Where(message =>
                message.Consumer != null &&
                message.Consumer.ConsumerKindId == (int)WebhookConsumerKind.Tenant &&
                message.Consumer.TenantId == ownership.TenantId),
            WebhookConsumerKind.Organization => query.Where(message =>
                message.Consumer != null &&
                message.Consumer.ConsumerKindId == (int)WebhookConsumerKind.Organization &&
                message.Consumer.TenantId == ownership.TenantId &&
                message.Consumer.OrganizationId == ownership.OrganizationId),
            WebhookConsumerKind.Group => query.Where(message =>
                message.Consumer != null &&
                message.Consumer.ConsumerKindId == (int)WebhookConsumerKind.Group &&
                message.Consumer.TenantId == ownership.TenantId &&
                message.Consumer.GroupId == ownership.GroupId),
            WebhookConsumerKind.User => query.Where(message =>
                message.Consumer != null &&
                message.Consumer.ConsumerKindId == (int)WebhookConsumerKind.User &&
                message.Consumer.TenantId == ownership.TenantId &&
                message.Consumer.OwnerUserId == ownership.UserId),
            _ => query.Where(_ => false)
        };
}
