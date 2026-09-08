using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Events;

public interface IEventSessionLanguageService
{
    Task<ICollection<EventSessionLanguageListDto>> GetLanguagesBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<ICollection<EventSessionLanguageListDto>> GetManagedLanguagesBySessionAsync(
        Guid eventId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<bool> SyncLanguagesForSessionAsync(
        Guid eventId,
        Guid sessionId,
        IEnumerable<int> languageIds,
        CancellationToken cancellationToken = default);
}
