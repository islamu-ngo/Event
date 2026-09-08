namespace Explore.Application.Contracts.Services;

public sealed record NotificationPreferenceResolveRequest(
    Guid TenantId,
    Guid UserId,
    Guid? OrganizationId,
    Guid? GroupId,
    string CategoryCode,
    string ChannelCode);

public sealed record NotificationPreferenceDecision(
    string CategoryCode,
    string ChannelCode,
    bool IsEnabled,
    bool IsRequired,
    bool IsLocked,
    bool IsMuted,
    string EffectiveSourceScope,
    string? LockReason);

public interface INotificationPreferenceResolver
{
    Task<NotificationPreferenceDecision> ResolveAsync(
        NotificationPreferenceResolveRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationPreferenceDecision>> ResolveBatchAsync(
        IReadOnlyCollection<NotificationPreferenceResolveRequest> requests,
        CancellationToken cancellationToken = default);
}
