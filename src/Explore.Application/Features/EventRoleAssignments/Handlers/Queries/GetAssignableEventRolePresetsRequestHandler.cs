using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventRoleAssignment;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;

namespace Explore.Application.Features.EventRoleAssignments.Handlers.Queries;

public sealed class GetAssignableEventRolePresetsRequestHandler
    : IQueryHandler<GetAssignableEventRolePresetsRequest, List<EventRolePresetDto>>
{
    private readonly IEventRoleAuthorityCeilingService _authorityCeilingService;

    public GetAssignableEventRolePresetsRequestHandler(IEventRoleAuthorityCeilingService authorityCeilingService)
    {
        _authorityCeilingService = authorityCeilingService;
    }

    public async Task<List<EventRolePresetDto>> QueryAsync(
        GetAssignableEventRolePresetsRequest query,
        CancellationToken cancellationToken = default)
    {
        var presets = await _authorityCeilingService.GetAssignableRolePresetsAsync(
            query.TenantId,
            query.EventId,
            query.AssignerUserId,
            cancellationToken);

        return presets
            .Select(preset => new EventRolePresetDto
            {
                RoleId = preset.RoleId,
                MasterCode = preset.MasterCode,
                FullName = preset.FullName,
                Description = preset.Description,
                PermissionCodes = preset.PermissionCodes
            })
            .ToList();
    }
}
