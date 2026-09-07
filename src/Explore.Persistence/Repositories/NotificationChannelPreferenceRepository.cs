using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class NotificationChannelPreferenceRepository
    : GenericRepository<NotificationChannelPreference, Guid>, INotificationChannelPreferenceRepository
{
    private readonly ExploreDbContext _dbContext;

    public NotificationChannelPreferenceRepository(ExploreDbContext dbContext)
        : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<NotificationPreferenceCategory>> ListCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.NotificationPreferenceCategories
            .AsNoTracking()
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationPreferenceChannel>> ListChannelsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.NotificationPreferenceChannels
            .AsNoTracking()
            .OrderBy(channel => channel.SortOrder)
            .ThenBy(channel => channel.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationChannelPreference>> ListForUserContextAsync(
        Guid tenantId,
        Guid userId,
        Guid? organizationId,
        Guid? groupId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.NotificationChannelPreferences
            .AsNoTracking()
            .Include(preference => preference.Category)
            .Include(preference => preference.Channel)
            .Where(preference => preference.TenantId == tenantId)
            .Where(preference =>
                preference.ScopeId == (int)ConfigurationScopeEnum.System
                || preference.ScopeId == (int)ConfigurationScopeEnum.Instance
                || preference.ScopeId == (int)ConfigurationScopeEnum.Tenant
                || (preference.ScopeId == (int)ConfigurationScopeEnum.Organization && preference.OrganizationId == organizationId)
                || (preference.ScopeId == (int)ConfigurationScopeEnum.Group && preference.GroupId == groupId)
                || (preference.ScopeId == (int)ConfigurationScopeEnum.User && preference.UserId == userId))
            .ToListAsync(cancellationToken);
    }

    public async Task<NotificationChannelPreference> UpsertUserPreferenceAsync(
        Guid tenantId,
        Guid userId,
        int categoryId,
        int channelId,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        var preference = await _dbContext.NotificationChannelPreferences
            .FirstOrDefaultAsync(preference =>
                preference.TenantId == tenantId
                && preference.ScopeId == (int)ConfigurationScopeEnum.User
                && preference.UserId == userId
                && preference.CategoryId == categoryId
                && preference.ChannelId == channelId,
                cancellationToken);

        if (preference is null)
        {
            preference = new NotificationChannelPreference
            {
                TenantId = tenantId,
                Tenant = null!,
                ScopeId = (int)ConfigurationScopeEnum.User,
                Scope = null!,
                UserId = userId,
                User = null!,
                CategoryId = categoryId,
                Category = null!,
                ChannelId = channelId,
                Channel = null!,
                IsEnabled = isEnabled
            };

            _dbContext.NotificationChannelPreferences.Add(preference);
        }
        else
        {
            preference.IsEnabled = isEnabled;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return preference;
    }

    public Task<NotificationChannelPreference> UpsertOrganizationPreferenceAsync(
        Guid tenantId,
        Guid organizationId,
        int categoryId,
        int channelId,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        return UpsertScopedPreferenceAsync(
            tenantId,
            (int)ConfigurationScopeEnum.Organization,
            userId: null,
            organizationId,
            groupId: null,
            categoryId,
            channelId,
            isEnabled,
            cancellationToken);
    }

    public Task<NotificationChannelPreference> UpsertGroupPreferenceAsync(
        Guid tenantId,
        Guid groupId,
        int categoryId,
        int channelId,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        return UpsertScopedPreferenceAsync(
            tenantId,
            (int)ConfigurationScopeEnum.Group,
            userId: null,
            organizationId: null,
            groupId,
            categoryId,
            channelId,
            isEnabled,
            cancellationToken);
    }

    private async Task<NotificationChannelPreference> UpsertScopedPreferenceAsync(
        Guid tenantId,
        int scopeId,
        Guid? userId,
        Guid? organizationId,
        Guid? groupId,
        int categoryId,
        int channelId,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        var preference = await _dbContext.NotificationChannelPreferences
            .FirstOrDefaultAsync(preference =>
                preference.TenantId == tenantId
                && preference.ScopeId == scopeId
                && preference.UserId == userId
                && preference.OrganizationId == organizationId
                && preference.GroupId == groupId
                && preference.CategoryId == categoryId
                && preference.ChannelId == channelId,
                cancellationToken);

        if (preference is null)
        {
            preference = new NotificationChannelPreference
            {
                TenantId = tenantId,
                Tenant = null!,
                ScopeId = scopeId,
                Scope = null!,
                UserId = userId,
                User = null,
                OrganizationId = organizationId,
                Organization = null,
                GroupId = groupId,
                Group = null,
                CategoryId = categoryId,
                Category = null!,
                ChannelId = channelId,
                Channel = null!,
                IsEnabled = isEnabled
            };

            _dbContext.NotificationChannelPreferences.Add(preference);
        }
        else
        {
            preference.IsEnabled = isEnabled;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return preference;
    }
}
