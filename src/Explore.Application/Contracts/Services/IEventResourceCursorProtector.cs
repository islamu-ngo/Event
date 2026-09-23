namespace Explore.Application.Contracts.Services;

public sealed record EventResourceCursorScope(
    Guid TenantId, Guid EventId, Guid? SubjectUserId, bool IsMachineCaller);

public sealed record EventResourceCursorPosition(int SortOrder, Guid ResourceId);

public interface IEventResourceCursorProtector
{
    string Protect(EventResourceCursorScope scope, EventResourceCursorPosition position);
    bool TryUnprotect(string cursor, EventResourceCursorScope scope, out EventResourceCursorPosition? position);
}
