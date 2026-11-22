using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings;
using Explore.Application.Notifications;
using Explore.Domain.Settings;

namespace Explore.Application.Features.Announcements.Requests.Commands;

public sealed class UpdateInstanceAnnouncementCommandHandler(
    IHumanSettingsMutationPreparation preparation,
    ITypedSettingsOriginalStore store,
    IHierarchicalSettingsResolver resolver,
    IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers)
    : ICommandHandler<UpdateInstanceAnnouncementCommand, AnnouncementPublicationResult>
{
    public async Task<AnnouncementPublicationResult> ExecuteAsync(
        UpdateInstanceAnnouncementCommand request, CancellationToken cancellationToken) =>
        await AnnouncementPublicationExecution.ExecuteAsync(
            await preparation.PrepareAsync(request, cancellationToken),
            preparation, store, resolver, notificationHandlers, cancellationToken);
}

public sealed class UpdateTenantAnnouncementCommandHandler(
    IHumanSettingsMutationPreparation preparation,
    ITypedSettingsOriginalStore store,
    IHierarchicalSettingsResolver resolver,
    IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers)
    : ICommandHandler<UpdateTenantAnnouncementCommand, AnnouncementPublicationResult>
{
    public async Task<AnnouncementPublicationResult> ExecuteAsync(
        UpdateTenantAnnouncementCommand request, CancellationToken cancellationToken) =>
        await AnnouncementPublicationExecution.ExecuteAsync(
            await preparation.PrepareAsync(request, cancellationToken),
            preparation, store, resolver, notificationHandlers, cancellationToken);
}

public sealed class UpdateManagedTenantAnnouncementCommandHandler(
    IHumanSettingsMutationPreparation preparation,
    ITypedSettingsOriginalStore store,
    IHierarchicalSettingsResolver resolver,
    IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers)
    : ICommandHandler<UpdateManagedTenantAnnouncementCommand, AnnouncementPublicationResult>
{
    public async Task<AnnouncementPublicationResult> ExecuteAsync(
        UpdateManagedTenantAnnouncementCommand request, CancellationToken cancellationToken) =>
        await AnnouncementPublicationExecution.ExecuteAsync(
            await preparation.PrepareAsync(request, cancellationToken),
            preparation, store, resolver, notificationHandlers, cancellationToken);
}

internal static class AnnouncementPublicationExecution
{
    internal static async Task<AnnouncementPublicationResult> ExecuteAsync(
        IPreparedHumanSettingsMutation prepared, IHumanSettingsMutationPreparation preparation,
        ITypedSettingsOriginalStore store, IHierarchicalSettingsResolver resolver,
        IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers,
        CancellationToken cancellationToken)
    {
        var (result, notifications) = await preparation.ExecutePreparedAsync(prepared,
            (_, token) => store.ApplyAnnouncementAsync(preparation.CurrentTypedOriginal
                ?? throw new UnauthorizedAccessException("The original announcement owner is required."), token),
            cancellationToken);
        if (!notifications.IsEmpty)
        {
            resolver.InvalidateCache(result.Source.Owner.TenantId.HasValue ? SettingScope.Tenant : SettingScope.Instance,
                result.Source.Owner.TenantId);
            foreach (var notification in notifications)
                await notificationHandlers.HandleAsync(notification, CancellationToken.None);
        }
        return result;
    }
}
