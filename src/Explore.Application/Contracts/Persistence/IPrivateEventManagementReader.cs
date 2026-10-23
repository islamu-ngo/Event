using Explore.Domain;
using Explore.Domain.Federation;
using Explore.Application.Contracts.Services;
using Explore.Application.Authorization;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Closed private reads on the original authenticated human. No principal, SQL, connection,
/// or authorization decision can be supplied by the caller.
/// </summary>
public interface IPrivateEventManagementReader
{
    Task<PrivateEventManagementGraph> ReadAsync(PrivateEventManagementRead request, CancellationToken cancellationToken);
    Task<PrivateEventManagementGraph> ReadTeamAsync(GetEventTeamListRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<Event>> ReadAuthorizationTargetsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken);
    Task<EventAuthoritySnapshot> ReadAuthorityAsync(Guid tenantId, Guid userId,
        IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken);
    Task<PrivateEventManagementGraph> ReadSessionHistoryAsync(Guid sessionId, int pageNumber, int pageSize, CancellationToken cancellationToken);
}

public enum PrivateEventManagementReadKind
{
    Details,
    Readiness,
    Program,
    CustomProperties,
    Registrations,
    MyEvents,
    Templates,
    SessionTemplates,
    EventSyncHistory,
    SessionSyncHistory,
    AuthorizationTarget,
    SessionAuthorizationTarget,
    Team,
    SessionSyncContext
}

public sealed record PrivateEventManagementRead(
    PrivateEventManagementReadKind Kind,
    Guid? EventId = null,
    Guid? SessionId = null,
    int PageNumber = 1,
    int PageSize = 50,
    int? EventTypeId = null,
    Guid? ExpectedUserId = null);

/// <summary>Entity graphs only; native CQS handlers retain DTO mapping and HAL contracts.</summary>
public sealed class PrivateEventManagementGraph
{
    public List<Event> Events { get; } = [];
    public List<PdsSyncOutbox> DeliveryStates { get; } = [];
    public List<Tag> Tags { get; } = [];
    public List<Category> Categories { get; } = [];
    public List<EventModerationRecord> ModerationRecords { get; } = [];
    public List<EventLocation> Locations { get; } = [];
    public List<EventSession> Sessions { get; } = [];
    public List<EventSessionGroup> SessionGroups { get; } = [];
    public List<EventDay> Days { get; } = [];
    public List<EventAgendaItem> AgendaItems { get; } = [];
    public List<EventCustomPropertyDefinition> Definitions { get; } = [];
    public List<EventCustomPropertyValue> Values { get; } = [];
    public List<RegistrationOrder> Registrations { get; } = [];
    public List<EventRoleAssignment> Team { get; } = [];
    public EventAuthorityForUser? TeamAuthority { get; set; }
    public List<EventRolePreset> TeamPresets { get; } = [];
    public List<EventTemplate> Templates { get; } = [];
    public List<EventSessionTemplate> SessionTemplates { get; } = [];
    public List<AuditLog> History { get; } = [];
    public int TotalCount { get; set; }
}

/// <summary>Requires an already acquired original human Serializable source-set owner.</summary>
public interface IPrivateEventManagementRepository
{
    Task<PrivateEventManagementGraph> ReadAsync(PrivateEventManagementRead request, CancellationToken cancellationToken);
}

/// <summary>Only the scoped original reader can activate a closed read; repositories can only verify it.</summary>
public interface IPrivateEventManagementReadCustody
{
    void RequireActive(PrivateEventManagementRead request);
}
