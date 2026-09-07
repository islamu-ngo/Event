using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models;

namespace Explore.Blazor.Client.Contracts.Services;

public interface IEventSessionService
{
    Task<PaginatedResult<EventSessionListDto>> GetSessionsPagedAsync(int pageNumber, int pageSize);
    Task<ICollection<EventSessionListDto>> GetAllSessionsAsync();
    Task<ICollection<EventSessionListDto>> GetSessionsByEventAsync(
        Guid eventId,
        bool includeManagedSessions = false,
        CancellationToken cancellationToken = default);
    Task<EventSessionDto?> GetSessionByIdAsync(Guid sessionId);
    Task<EventSessionDto?> GetManagedSessionByIdAsync(Guid eventId, Guid sessionId);
    Task<BaseCommandResponseOfGuid> CreateSessionAsync(CreateEventSessionDto session);
    Task<BaseCommandResponseOfGuid> UpdateSessionAsync(
        Guid sessionId,
        Guid expectedConcurrencyStamp,
        UpdateEventSessionDto session);
    Task<BaseCommandResponseOfGuid?> PublishEventSessionAsync(Guid sessionId, Guid expectedConcurrencyStamp, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid?> ArchiveEventSessionAsync(Guid sessionId, Guid expectedConcurrencyStamp, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid?> CancelEventSessionAsync(Guid sessionId, Guid expectedConcurrencyStamp, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid?> CompleteEventSessionAsync(Guid sessionId, Guid expectedConcurrencyStamp, CancellationToken cancellationToken = default);
    Task<bool> DeleteSessionAsync(Guid sessionId);
    Task<ICollection<HalResourceOfEventSessionGroupListDto>> GetSessionGroupsByEventAsync(Guid eventId);
    Task<ICollection<HalResourceOfEventSessionGroupListDto>> GetManagedSessionGroupsByEventAsync(Guid eventId);
    Task<BaseCommandResponseOfGuid> CreateSessionGroupAsync(CreateEventSessionGroupRequestDto group);
    Task<BaseCommandResponseOfGuid> UpdateSessionGroupAsync(
        Guid sessionGroupId,
        Guid expectedConcurrencyStamp,
        UpdateEventSessionGroupRequestDto group);
    Task<bool> DeleteSessionGroupAsync(Guid eventId, Guid sessionGroupId);
    Task<BaseCommandResponseOfGuid> AssignSessionToGroupAsync(
        Guid eventId,
        Guid eventSessionGroupId,
        Guid eventSessionId,
        bool isPrimary = true,
        int sortOrder = 0);
    Task<BaseCommandResponseOfGuid> UnassignSessionFromGroupAsync(
        Guid eventId,
        Guid eventSessionGroupId,
        Guid eventSessionId);
    Task<EventSessionCreateContextDto?> GetEventSessionCreateContextAsync(Guid eventId, CancellationToken cancellationToken = default);
}
