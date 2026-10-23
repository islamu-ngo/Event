using Explore.Application.Features.EventRoleAssignments.Requests.Queries;

namespace Explore.Application.DTOs.EventRoleAssignment;

public sealed record ManagedEventTeamDto(
    IReadOnlyList<EventTeamMemberDto> Members,
    CurrentUserEventPermissionsDto Permissions,
    IReadOnlyList<EventRolePresetDto> AssignablePresets);
