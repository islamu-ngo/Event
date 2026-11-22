using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services;

public interface IAnnouncementClientService : IAsyncDisposable
{
    event Action? Changed;
    AnnouncementDisplayState? Display { get; }
    bool IsSaving { get; }
    bool RequiresReconciliation { get; }
    Task<AnnouncementDisplayState?> LoadAsync(bool reconcile = false, CancellationToken cancellationToken = default);
    Task<bool> DismissAsync(CancellationToken cancellationToken = default);
    Task<HalResourceOfAnnouncementSourceResource?> LoadSourceAsync(
        AnnouncementEditScope scope = AnnouncementEditScope.Tenant, Guid? managedTenantId = null,
        CancellationToken cancellationToken = default);
    bool CanPublish(HalResourceOfAnnouncementSourceResource? source);
    Task<AnnouncementPublicationClientResult> PublishAsync(
        HalResourceOfAnnouncementSourceResource source, AnnouncementChangeDto change,
        CancellationToken cancellationToken = default);
}
