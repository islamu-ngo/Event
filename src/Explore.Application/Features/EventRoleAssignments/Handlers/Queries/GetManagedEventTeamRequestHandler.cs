using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventRoleAssignment;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;

namespace Explore.Application.Features.EventRoleAssignments.Handlers.Queries;

public sealed class GetManagedEventTeamRequestHandler(
    IPrivateEventManagementReader reader, ITenantContext tenant, TimeProvider clock)
    : IQueryHandler<GetManagedEventTeamRequest, ManagedEventTeamDto>
{
    public async Task<ManagedEventTeamDto> QueryAsync(
        GetManagedEventTeamRequest request, CancellationToken cancellationToken)
    {
        var graph = await reader.ReadTeamAsync(new()
        {
            TenantId = tenant.TenantId,
            EventId = request.EventId,
            IncludeInactive = request.IncludeInactive
        }, cancellationToken);
        var authority = graph.TeamAuthority
            ?? throw new InvalidOperationException("The acquired team read must retain its original authority.");
        var now = clock.GetUtcNow().UtcDateTime;
        var members = graph.Team.Where(row => request.IncludeInactive || row.IsEffectiveAt(now))
            .OrderBy(row => row.RoleId).ThenBy(row => row.User.Pii.FirstName, StringComparer.Ordinal)
            .ThenBy(row => row.User.Pii.LastName, StringComparer.Ordinal).ThenBy(row => row.Id);
        var permissions = new CurrentUserEventPermissionsDto
        {
            EventId = request.EventId,
            HasAnyRole = authority.RoleCodes.Count > 0,
            IsOwner = authority.IsOwner,
            IsManager = authority.IsManager,
            RoleCodes = authority.RoleCodes,
            PermissionCodes = authority.PermissionCodes
        };
        var presets = graph.TeamPresets.Select(preset => new EventRolePresetDto
        {
            RoleId = preset.RoleId,
            MasterCode = preset.MasterCode,
            FullName = preset.FullName,
            Description = preset.Description,
            PermissionCodes = preset.PermissionCodes
        }).ToList();
        return new(GetEventTeamListRequestHandler.Map(members, now), permissions, presets);
    }
}
