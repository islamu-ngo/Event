using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Events;

public interface IEventDayService
{
    Task<ICollection<EventDayListDto>> GetDaysByEventAsync(
        Guid eventId,
        bool includeManaged = false,
        CancellationToken cancellationToken = default);
    Task<EventDayDto?> GetDayByIdAsync(Guid dayId);
    Task<BaseCommandResponseOfGuid?> CreateDayAsync(CreateEventDayDto dto);
    Task<BaseCommandResponseOfGuid?> UpdateDayAsync(Guid id, Guid expectedConcurrencyStamp, UpdateEventDayDto dto);
    Task<bool> DeleteDayAsync(Guid dayId);
}
