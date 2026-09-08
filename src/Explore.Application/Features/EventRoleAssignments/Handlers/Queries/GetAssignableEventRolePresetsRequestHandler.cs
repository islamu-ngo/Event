using Explore.Application.Authorization;
using Explore.Application.DTOs.EventRoleAssignment;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventRoleAssignments.Handlers.Queries;

public sealed class GetAssignableEventRolePresetsRequestHandler
    : IRequestHandler<GetAssignableEventRolePresetsRequest, List<EventRolePresetDto>>
{
    private readonly IEventRoleAuthorityCeilingService _authorityCeilingService;

    public GetAssignableEventRolePresetsRequestHandler(IEventRoleAuthorityCeilingService authorityCeilingService)
    {
        _authorityCeilingService = authorityCeilingService;
    }

    public async Task<List<EventRolePresetDto>> Handle(
        GetAssignableEventRolePresetsRequest request,
        CancellationToken cancellationToken)
    {
        var presets = await _authorityCeilingService.GetAssignableRolePresetsAsync(
            request.TenantId,
            request.EventId,
            request.AssignerUserId,
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
