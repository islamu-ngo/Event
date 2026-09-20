using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;

namespace Explore.Application.Features.EventRoleAssignments.Handlers.Queries;

public sealed class GetCurrentUserEventPermissionsRequestHandler
    : IQueryHandler<GetCurrentUserEventPermissionsRequest, CurrentUserEventPermissionsDto>
{
    private readonly IEventAuthoritySnapshotService _eventAuthoritySnapshotService;

    public GetCurrentUserEventPermissionsRequestHandler(IEventAuthoritySnapshotService eventAuthoritySnapshotService)
    {
        _eventAuthoritySnapshotService = eventAuthoritySnapshotService;
    }

    public async Task<CurrentUserEventPermissionsDto> QueryAsync(
        GetCurrentUserEventPermissionsRequest query,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _eventAuthoritySnapshotService.GetForUserAndEventsAsync(
            query.TenantId,
            query.UserId,
            new[] { query.EventId },
            cancellationToken);

        if (!snapshot.Events.TryGetValue(query.EventId, out var authority))
        {
            return new CurrentUserEventPermissionsDto
            {
                EventId = query.EventId,
                HasAnyRole = false,
                IsOwner = false,
                IsManager = false,
                RoleCodes = new HashSet<string>(),
                PermissionCodes = new HashSet<string>()
            };
        }

        return new CurrentUserEventPermissionsDto
        {
            EventId = query.EventId,
            HasAnyRole = authority.RoleCodes.Count > 0,
            IsOwner = authority.IsOwner,
            IsManager = authority.IsManager,
            RoleCodes = authority.RoleCodes,
            PermissionCodes = authority.PermissionCodes
        };
    }
}
