using Explore.Application.Features.EventCustomPropertyProjections.Requests.Commands;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Commands;
using Explore.Application.Features.LocationRooms.Requests.Commands;

namespace Explore.Application.Contracts.Persistence;

public partial interface IHumanAdministrativeActionPreparation
{
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(DrainCustomPropertyProjectionDirtyScopesCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(RebuildEventCustomPropertyProjectionCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(RebuildSingleEventCustomPropertyProjectionCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(RebuildEventSessionCustomPropertyProjectionCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(RebuildSingleEventSessionCustomPropertyProjectionCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(UpdateLocationRoomCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
}
