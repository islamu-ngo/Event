using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventRoleAssignments.Handlers.Queries;

public sealed class GetCurrentUserEventPermissionsRequestHandler
    : IRequestHandler<GetCurrentUserEventPermissionsRequest, CurrentUserEventPermissionsDto>
{
    private readonly IEventAuthoritySnapshotService _eventAuthoritySnapshotService;

    public GetCurrentUserEventPermissionsRequestHandler(IEventAuthoritySnapshotService eventAuthoritySnapshotService)
    {
        _eventAuthoritySnapshotService = eventAuthoritySnapshotService;
    }

    public async Task<CurrentUserEventPermissionsDto> Handle(
        GetCurrentUserEventPermissionsRequest request,
        CancellationToken cancellationToken)
    {
        var snapshot = await _eventAuthoritySnapshotService.GetForUserAndEventsAsync(
            request.TenantId,
            request.UserId,
            new[] { request.EventId },
            cancellationToken);

        if (!snapshot.Events.TryGetValue(request.EventId, out var authority))
        {
            return new CurrentUserEventPermissionsDto
            {
                EventId = request.EventId,
                HasAnyRole = false,
                IsOwner = false,
                IsManager = false,
                RoleCodes = new HashSet<string>(),
                PermissionCodes = new HashSet<string>()
            };
        }

        return new CurrentUserEventPermissionsDto
        {
            EventId = request.EventId,
            HasAnyRole = authority.RoleCodes.Count > 0,
            IsOwner = authority.IsOwner,
            IsManager = authority.IsManager,
            RoleCodes = authority.RoleCodes,
            PermissionCodes = authority.PermissionCodes
        };
    }
}
