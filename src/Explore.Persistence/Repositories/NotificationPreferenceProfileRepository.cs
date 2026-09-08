using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class NotificationPreferenceProfileRepository
    : GenericRepository<NotificationPreferenceProfile, Guid>, INotificationPreferenceProfileRepository
{
    private readonly ExploreDbContext _dbContext;

    public NotificationPreferenceProfileRepository(ExploreDbContext dbContext)
        : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<NotificationPreferenceProfile>> ListForUserContextAsync(
        Guid tenantId,
        Guid userId,
        Guid? organizationId,
        Guid? groupId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.NotificationPreferenceProfiles
            .AsNoTracking()
            .Where(profile => profile.TenantId == tenantId)
            .Where(profile =>
                profile.ScopeId == (int)ConfigurationScopeEnum.System
                || profile.ScopeId == (int)ConfigurationScopeEnum.Instance
                || profile.ScopeId == (int)ConfigurationScopeEnum.Tenant
                || (profile.ScopeId == (int)ConfigurationScopeEnum.Organization && profile.OrganizationId == organizationId)
                || (profile.ScopeId == (int)ConfigurationScopeEnum.Group && profile.GroupId == groupId)
                || (profile.ScopeId == (int)ConfigurationScopeEnum.User && profile.UserId == userId))
            .ToListAsync(cancellationToken);
    }

    public async Task<NotificationPreferenceProfile> UpsertUserMuteAsync(
        Guid tenantId,
        Guid userId,
        bool isMuted,
        CancellationToken cancellationToken = default)
    {
        var profile = await _dbContext.NotificationPreferenceProfiles
            .FirstOrDefaultAsync(profile =>
                profile.TenantId == tenantId
                && profile.ScopeId == (int)ConfigurationScopeEnum.User
                && profile.UserId == userId,
                cancellationToken);

        if (profile is null)
        {
            profile = new NotificationPreferenceProfile
            {
                TenantId = tenantId,
                Tenant = null!,
                ScopeId = (int)ConfigurationScopeEnum.User,
                Scope = null!,
                UserId = userId,
                User = null!,
                IsMuted = isMuted
            };

            _dbContext.NotificationPreferenceProfiles.Add(profile);
        }
        else
        {
            profile.IsMuted = isMuted;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return profile;
    }

    public Task<NotificationPreferenceProfile> UpsertOrganizationMuteAsync(
        Guid tenantId,
        Guid organizationId,
        bool isMuted,
        CancellationToken cancellationToken = default)
    {
        return UpsertScopedMuteAsync(
            tenantId,
            (int)ConfigurationScopeEnum.Organization,
            userId: null,
            organizationId,
            groupId: null,
            isMuted,
            cancellationToken);
    }

    public Task<NotificationPreferenceProfile> UpsertGroupMuteAsync(
        Guid tenantId,
        Guid groupId,
        bool isMuted,
        CancellationToken cancellationToken = default)
    {
        return UpsertScopedMuteAsync(
            tenantId,
            (int)ConfigurationScopeEnum.Group,
            userId: null,
            organizationId: null,
            groupId,
            isMuted,
            cancellationToken);
    }

    private async Task<NotificationPreferenceProfile> UpsertScopedMuteAsync(
        Guid tenantId,
        int scopeId,
        Guid? userId,
        Guid? organizationId,
        Guid? groupId,
        bool isMuted,
        CancellationToken cancellationToken)
    {
        var profile = await _dbContext.NotificationPreferenceProfiles
            .FirstOrDefaultAsync(profile =>
                profile.TenantId == tenantId
                && profile.ScopeId == scopeId
                && profile.UserId == userId
                && profile.OrganizationId == organizationId
                && profile.GroupId == groupId,
                cancellationToken);

        if (profile is null)
        {
            profile = new NotificationPreferenceProfile
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
                IsMuted = isMuted
            };

            _dbContext.NotificationPreferenceProfiles.Add(profile);
        }
        else
        {
            profile.IsMuted = isMuted;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return profile;
    }
}
