using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Events;

public interface IEventAgendaItemService
{
    Task<ICollection<EventAgendaItemListDto>> GetAgendaItemsByEventAsync(Guid eventId);
    Task<ICollection<EventAgendaItemListDto>> GetManagedAgendaItemsByEventAsync(Guid eventId);
    Task<EventAgendaItemDto?> GetAgendaItemByIdAsync(Guid agendaItemId);
    Task<EventAgendaItemDto?> GetManagedAgendaItemByIdAsync(Guid eventId, Guid agendaItemId);
    Task<BaseCommandResponseOfGuid?> CreateAgendaItemAsync(CreateEventAgendaItemDto dto);
    Task<BaseCommandResponseOfGuid?> UpdateAgendaItemAsync(Guid id, Guid expectedConcurrencyStamp, UpdateEventAgendaItemDto dto);
    Task<bool> DeleteAgendaItemAsync(Guid agendaItemId);
}
