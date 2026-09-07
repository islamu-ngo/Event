using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.References;
using Explore.Persistence.Extensions;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class NotificationRepository : GenericRepository<Notification, Guid>, INotificationRepository
{
    private readonly ExploreDbContext _dbContext;

    public NotificationRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> ExistsByDeduplicationKeyAsync(Guid tenantId, Guid userId, string deduplicationKey, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Notifications
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .AnyAsync(notification => notification.TenantId == tenantId
                && notification.UserId == userId
                && notification.DeduplicationKey == deduplicationKey,
                cancellationToken);
    }

    public async Task<Notification?> GetByDeduplicationKeyAsync(Guid tenantId, Guid userId, string deduplicationKey, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Notifications
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .FirstOrDefaultAsync(notification => notification.TenantId == tenantId
                && notification.UserId == userId
                && notification.DeduplicationKey == deduplicationKey,
                cancellationToken);
    }

    public override async Task<Notification> Create(Notification entity)
    {
        EnsureRegisteredReference(entity);
        return await base.Create(entity);
    }

    public override async Task Update(Notification entity)
    {
        EnsureRegisteredReference(entity);
        await base.Update(entity);
    }

    public async Task<(List<Notification> Items, int TotalCount)> GetUserNotificationsPaged(
        Guid userId, int pageNumber, int pageSize, bool? isRead = null, int? notificationTypeId = null,
        int? notificationScopeId = null, int? notificationReasonId = null, bool? isArchived = null, bool? isSnoozed = null)
    {
        var query = _dbContext.Notifications
            .AsNoTracking()
            .IncludeStandardDetails()
            .Where(n => n.UserId == userId);

        if (isRead.HasValue)
            query = query.Where(n => n.IsRead == isRead.Value);

        if (notificationTypeId.HasValue)
            query = query.Where(n => n.NotificationTypeId == notificationTypeId.Value);

        if (notificationScopeId.HasValue)
            query = query.Where(n => n.NotificationScopeId == notificationScopeId.Value);

        if (notificationReasonId.HasValue)
            query = query.Where(n => n.NotificationReasonId == notificationReasonId.Value);

        if (isArchived.HasValue)
            query = query.Where(n => n.IsArchived == isArchived.Value);

        if (isSnoozed.HasValue)
        {
            var now = DateTime.UtcNow;
            query = isSnoozed.Value
                ? query.Where(n => n.SnoozedUntil != null && n.SnoozedUntil > now)
                : query.Where(n => n.SnoozedUntil == null || n.SnoozedUntil <= now);
        }

        query = query.OrderByDescending(n => n.CreatedAt);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<int> GetUnreadCount(Guid userId, int? notificationScopeId = null)
    {
        var query = _dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsRead);

        if (notificationScopeId.HasValue)
            query = query.Where(n => n.NotificationScopeId == notificationScopeId.Value);

        return await query.CountAsync();
    }

    public async Task<bool> MarkAsRead(Guid notificationId, Guid userId)
    {
        var notification = await _dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

        if (notification == null)
            return false;

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        return true;
    }

    public async Task<int> MarkAllAsRead(Guid userId, DateTime? cutoff = null)
    {
        var effectiveCutoff = cutoff ?? DateTime.UtcNow;

        return await _dbContext.Notifications
            .Where(n => n.UserId == userId && !n.IsRead && n.CreatedAt <= effectiveCutoff)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTime.UtcNow));
    }

    public async Task<Notification?> GetByIdForUser(Guid notificationId, Guid userId)
    {
        return await _dbContext.Notifications
            .AsNoTracking()
            .IncludeStandardDetails()
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);
    }

    public async Task<bool> ArchiveNotification(Guid notificationId, Guid userId, bool archive)
    {
        var notification = await _dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

        if (notification == null)
            return false;

        notification.IsArchived = archive;
        notification.ArchivedAt = archive ? DateTime.UtcNow : null;
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SnoozeNotification(Guid notificationId, Guid userId, DateTime? snoozedUntil)
    {
        var notification = await _dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

        if (notification == null)
            return false;

        notification.SnoozedUntil = snoozedUntil;
        await _dbContext.SaveChangesAsync();
        return true;
    }

    private static void EnsureRegisteredReference(Notification notification)
    {
        var errors = ReferenceTypeRegistry.ValidateNotificationReference(notification);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(' ', errors));
        }
    }
}
