using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventRoleAssignment;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;

namespace Explore.Application.Features.EventRoleAssignments.Handlers.Queries;

public sealed class GetAssignableEventRolePresetsRequestHandler
    : IQueryHandler<GetAssignableEventRolePresetsRequest, List<EventRolePresetDto>>
{
    private readonly IHumanEventTeamActionPreparation _preparation;
    private readonly ISerializableAttemptOwner _owner;

    public GetAssignableEventRolePresetsRequestHandler(IHumanEventTeamActionPreparation preparation,
        ISerializableAttemptOwner owner)
    {
        _preparation = preparation;
        _owner = owner;
    }

    public async Task<List<EventRolePresetDto>> QueryAsync(
        GetAssignableEventRolePresetsRequest query,
        CancellationToken cancellationToken = default)
    {
        var prepared = await _preparation.PrepareAsync(query, cancellationToken);
        return await _owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(query, token);
            return Map(prepared.GetAssignablePresets());
        }, cancellationToken);
    }

    private static List<EventRolePresetDto> Map(IReadOnlyList<EventRolePreset> presets) =>
        presets
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
