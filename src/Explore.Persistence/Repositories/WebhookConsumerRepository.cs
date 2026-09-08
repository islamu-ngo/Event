using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class WebhookConsumerRepository : IWebhookConsumerRepository
{
    private readonly ExploreDbContext _dbContext;

    public WebhookConsumerRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WebhookConsumer> CreateAsync(WebhookConsumer consumer, CancellationToken cancellationToken)
    {
        if (consumer.Id == Guid.Empty)
        {
            consumer.Id = Guid.CreateVersion7();
        }

        await _dbContext.WebhookConsumers.AddAsync(consumer, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return consumer;
    }

    public async Task<WebhookConsumer?> GetByIdForOwnerOperationAsync(
        Guid consumerId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        IQueryable<WebhookConsumer> query = _dbContext.WebhookConsumers
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookOwnerOperation)
            .Include(consumer => consumer.ProviderBindings);

        if (!forUpdate)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(
            consumer => consumer.Id == consumerId,
            cancellationToken);
    }

    public async Task<WebhookConsumer?> GetByOwnerAndIdAsync(
        WebhookOwnershipScope ownership,
        Guid consumerId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        IQueryable<WebhookConsumer> query = ApplyOwnerPredicate(
                _dbContext.WebhookConsumers
                    .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookOwnerOperation),
                ownership)
            .Include(consumer => consumer.ProviderBindings);

        if (!forUpdate)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(
            consumer => consumer.Id == consumerId,
            cancellationToken);
    }

    public async Task<WebhookConsumer?> GetByOwnerAndNameAsync(
        WebhookOwnershipScope ownership,
        string name,
        CancellationToken cancellationToken)
    {
        return await ApplyOwnerPredicate(
                _dbContext.WebhookConsumers
                    .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookOwnerOperation),
                ownership)
            .AsNoTracking()
            .FirstOrDefaultAsync(consumer => consumer.Name == name, cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookConsumer>> ListByOwnerAsync(
        WebhookOwnershipScope ownership,
        int limit,
        CancellationToken cancellationToken)
    {
        return await ApplyOwnerPredicate(
                _dbContext.WebhookConsumers
                    .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookOwnerOperation),
                ownership)
            .Include(consumer => consumer.ProviderBindings)
            .AsNoTracking()
            .OrderBy(consumer => consumer.Name)
            .ThenBy(consumer => consumer.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<WebhookConsumer?> GetByTenantAndIdAsync(
        Guid tenantId,
        Guid consumerId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.WebhookConsumers
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Include(e => e.ProviderBindings)
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == consumerId, cancellationToken);
    }

    public async Task<WebhookConsumer?> GetByTenantAndIdForUpdateAsync(
        Guid tenantId,
        Guid consumerId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.WebhookConsumers
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Include(consumer => consumer.ProviderBindings)
            .FirstOrDefaultAsync(
                consumer => consumer.TenantId == tenantId && consumer.Id == consumerId,
                cancellationToken);
    }

    public async Task<WebhookConsumer> UpdateAsync(
        WebhookConsumer consumer,
        CancellationToken cancellationToken)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
        return consumer;
    }

    public async Task<WebhookConsumer?> GetByTenantAndNameAsync(
        Guid tenantId,
        string name,
        CancellationToken cancellationToken)
    {
        return await _dbContext.WebhookConsumers
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Include(e => e.ProviderBindings)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.TenantId == tenantId && e.Name == name,
                cancellationToken);
    }

    public async Task<WebhookConsumer?> GetByExternalProviderAppIdAsync(
        Guid tenantId,
        string externalProviderAppId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.WebhookConsumers
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.TenantId == tenantId && e.ExternalProviderAppId == externalProviderAppId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookConsumer>> ListByTenantAsync(
        Guid tenantId,
        int limit,
        CancellationToken cancellationToken)
    {
        return await _dbContext.WebhookConsumers
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Include(e => e.ProviderBindings)
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.Name)
            .ThenBy(e => e.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<WebhookConsumer> ApplyOwnerPredicate(
        IQueryable<WebhookConsumer> query,
        WebhookOwnershipScope ownership) => ownership.Kind switch
        {
            WebhookConsumerKind.Instance => query.Where(consumer =>
                consumer.ConsumerKindId == (int)WebhookConsumerKind.Instance &&
                consumer.InstanceId == ownership.InstanceId &&
                consumer.TenantId == null),
            WebhookConsumerKind.Tenant => query.Where(consumer =>
                consumer.ConsumerKindId == (int)WebhookConsumerKind.Tenant &&
                consumer.TenantId == ownership.TenantId),
            WebhookConsumerKind.Organization => query.Where(consumer =>
                consumer.ConsumerKindId == (int)WebhookConsumerKind.Organization &&
                consumer.TenantId == ownership.TenantId &&
                consumer.OrganizationId == ownership.OrganizationId),
            WebhookConsumerKind.Group => query.Where(consumer =>
                consumer.ConsumerKindId == (int)WebhookConsumerKind.Group &&
                consumer.TenantId == ownership.TenantId &&
                consumer.GroupId == ownership.GroupId),
            WebhookConsumerKind.User => query.Where(consumer =>
                consumer.ConsumerKindId == (int)WebhookConsumerKind.User &&
                consumer.TenantId == ownership.TenantId &&
                consumer.OwnerUserId == ownership.UserId),
            _ => query.Where(_ => false)
        };
}
