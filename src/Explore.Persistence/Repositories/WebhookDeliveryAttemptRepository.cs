using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class WebhookDeliveryAttemptRepository(ExploreDbContext dbContext)
    : IWebhookDeliveryAttemptRepository
{
    public async Task<WebhookDeliveryAttempt> CreateAsync(
        WebhookDeliveryAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (attempt.Id == Guid.Empty)
        {
            attempt.Id = Guid.CreateVersion7();
        }

        await dbContext.WebhookDeliveryAttempts.AddAsync(attempt, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return attempt;
    }

    public async Task<IReadOnlyList<WebhookDeliveryAttempt>> CreateManyAsync(
        IReadOnlyCollection<WebhookDeliveryAttempt> attempts,
        CancellationToken cancellationToken)
    {
        if (attempts.Count == 0)
        {
            return [];
        }

        foreach (var attempt in attempts)
        {
            if (attempt.Id == Guid.Empty)
            {
                attempt.Id = Guid.CreateVersion7();
            }
        }

        await dbContext.WebhookDeliveryAttempts.AddRangeAsync(attempts, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return attempts.ToList();
    }

    public Task<WebhookDeliveryAttempt?> GetByIdForOwnerOperationAsync(
        Guid attemptId,
        CancellationToken cancellationToken) =>
        dbContext.WebhookDeliveryAttempts
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookOwnerOperation)
            .AsNoTracking()
            .Include(attempt => attempt.Endpoint)
            .ThenInclude(endpoint => endpoint!.Consumer)
            .Include(attempt => attempt.Message)
            .FirstOrDefaultAsync(attempt => attempt.Id == attemptId, cancellationToken);

    public async Task<IReadOnlyList<WebhookDeliveryAttempt>> ListByOwnerAsync(
        WebhookOwnershipScope ownership,
        Guid? messageId,
        Guid? endpointId,
        int limit,
        CancellationToken cancellationToken)
    {
        IQueryable<WebhookDeliveryAttempt> query = ApplyOwnerPredicate(
                dbContext.WebhookDeliveryAttempts
                    .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookOwnerOperation),
                ownership)
            .AsNoTracking()
            .Include(attempt => attempt.Endpoint)
            .ThenInclude(endpoint => endpoint!.Consumer)
            .Include(attempt => attempt.Message);

        if (messageId.HasValue)
        {
            query = query.Where(attempt => attempt.MessageId == messageId.Value);
        }

        if (endpointId.HasValue)
        {
            query = query.Where(attempt => attempt.EndpointId == endpointId.Value);
        }

        return await query
            .OrderByDescending(attempt => attempt.CreatedAt)
            .ThenByDescending(attempt => attempt.ScheduledAt)
            .ThenByDescending(attempt => attempt.AttemptNumber)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookDeliveryAttempt>> GetByMessageAsync(
        Guid tenantId,
        Guid messageId,
        CancellationToken cancellationToken) =>
        await dbContext.WebhookDeliveryAttempts
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Where(attempt => attempt.TenantId == tenantId && attempt.MessageId == messageId)
            .OrderBy(attempt => attempt.EndpointId)
            .ThenBy(attempt => attempt.AttemptNumber)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WebhookDeliveryAttempt>> ListByTenantAsync(
        Guid tenantId,
        Guid? messageId,
        Guid? endpointId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.WebhookDeliveryAttempts
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Include(attempt => attempt.Endpoint)
            .ThenInclude(endpoint => endpoint!.Consumer)
            .Include(attempt => attempt.Message)
            .Where(attempt => attempt.TenantId == tenantId);

        if (messageId.HasValue)
        {
            query = query.Where(attempt => attempt.MessageId == messageId.Value);
        }

        if (endpointId.HasValue)
        {
            query = query.Where(attempt => attempt.EndpointId == endpointId.Value);
        }

        return await query
            .OrderByDescending(attempt => attempt.CreatedAt)
            .ThenByDescending(attempt => attempt.ScheduledAt)
            .ThenByDescending(attempt => attempt.AttemptNumber)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<WebhookDeliveryAttempt?> GetByTenantAndIdAsync(
        Guid tenantId,
        Guid attemptId,
        CancellationToken cancellationToken) =>
        dbContext.WebhookDeliveryAttempts
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Include(attempt => attempt.Endpoint)
            .ThenInclude(endpoint => endpoint!.Consumer)
            .Include(attempt => attempt.Message)
            .FirstOrDefaultAsync(
                attempt => attempt.TenantId == tenantId && attempt.Id == attemptId,
                cancellationToken);

    private static IQueryable<WebhookDeliveryAttempt> ApplyOwnerPredicate(
        IQueryable<WebhookDeliveryAttempt> query,
        WebhookOwnershipScope ownership) => ownership.Kind switch
        {
            WebhookConsumerKind.Instance => query.Where(attempt =>
                attempt.Endpoint != null &&
                attempt.Endpoint.Consumer != null &&
                attempt.Endpoint.Consumer.ConsumerKindId == (int)WebhookConsumerKind.Instance &&
                attempt.Endpoint.Consumer.InstanceId == ownership.InstanceId),
            WebhookConsumerKind.Tenant => query.Where(attempt =>
                attempt.Endpoint != null &&
                attempt.Endpoint.Consumer != null &&
                attempt.Endpoint.Consumer.ConsumerKindId == (int)WebhookConsumerKind.Tenant &&
                attempt.Endpoint.Consumer.TenantId == ownership.TenantId),
            WebhookConsumerKind.Organization => query.Where(attempt =>
                attempt.Endpoint != null &&
                attempt.Endpoint.Consumer != null &&
                attempt.Endpoint.Consumer.ConsumerKindId == (int)WebhookConsumerKind.Organization &&
                attempt.Endpoint.Consumer.TenantId == ownership.TenantId &&
                attempt.Endpoint.Consumer.OrganizationId == ownership.OrganizationId),
            WebhookConsumerKind.Group => query.Where(attempt =>
                attempt.Endpoint != null &&
                attempt.Endpoint.Consumer != null &&
                attempt.Endpoint.Consumer.ConsumerKindId == (int)WebhookConsumerKind.Group &&
                attempt.Endpoint.Consumer.TenantId == ownership.TenantId &&
                attempt.Endpoint.Consumer.GroupId == ownership.GroupId),
            WebhookConsumerKind.User => query.Where(attempt =>
                attempt.Endpoint != null &&
                attempt.Endpoint.Consumer != null &&
                attempt.Endpoint.Consumer.ConsumerKindId == (int)WebhookConsumerKind.User &&
                attempt.Endpoint.Consumer.TenantId == ownership.TenantId &&
                attempt.Endpoint.Consumer.OwnerUserId == ownership.UserId),
            _ => query.Where(_ => false)
        };
}
