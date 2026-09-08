using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface INotificationPreferenceProfileRepository : IGenericRepository<NotificationPreferenceProfile, Guid>
{
    Task<IReadOnlyList<NotificationPreferenceProfile>> ListForUserContextAsync(
        Guid tenantId,
        Guid userId,
        Guid? organizationId,
        Guid? groupId,
        CancellationToken cancellationToken = default);

    Task<NotificationPreferenceProfile> UpsertUserMuteAsync(
        Guid tenantId,
        Guid userId,
        bool isMuted,
        CancellationToken cancellationToken = default);

    Task<NotificationPreferenceProfile> UpsertOrganizationMuteAsync(
        Guid tenantId,
        Guid organizationId,
        bool isMuted,
        CancellationToken cancellationToken = default);

    Task<NotificationPreferenceProfile> UpsertGroupMuteAsync(
        Guid tenantId,
        Guid groupId,
        bool isMuted,
        CancellationToken cancellationToken = default);
}
