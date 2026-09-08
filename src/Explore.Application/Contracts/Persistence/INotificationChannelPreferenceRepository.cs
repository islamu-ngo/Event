using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface INotificationChannelPreferenceRepository : IGenericRepository<NotificationChannelPreference, Guid>
{
    Task<IReadOnlyList<NotificationPreferenceCategory>> ListCategoriesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationPreferenceChannel>> ListChannelsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationChannelPreference>> ListForUserContextAsync(
        Guid tenantId,
        Guid userId,
        Guid? organizationId,
        Guid? groupId,
        CancellationToken cancellationToken = default);

    Task<NotificationChannelPreference> UpsertUserPreferenceAsync(
        Guid tenantId,
        Guid userId,
        int categoryId,
        int channelId,
        bool isEnabled,
        CancellationToken cancellationToken = default);

    Task<NotificationChannelPreference> UpsertOrganizationPreferenceAsync(
        Guid tenantId,
        Guid organizationId,
        int categoryId,
        int channelId,
        bool isEnabled,
        CancellationToken cancellationToken = default);

    Task<NotificationChannelPreference> UpsertGroupPreferenceAsync(
        Guid tenantId,
        Guid groupId,
        int categoryId,
        int channelId,
        bool isEnabled,
        CancellationToken cancellationToken = default);
}
