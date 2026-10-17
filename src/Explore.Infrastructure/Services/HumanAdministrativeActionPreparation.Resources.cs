using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Commands;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Commands;
using Explore.Application.Features.LocationRooms.Requests.Commands;
using System.Text.Json;

namespace Explore.Infrastructure.Services;

public sealed partial class HumanAdministrativeActionPreparation
{
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(DrainCustomPropertyProjectionDirtyScopesCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareResourceActionAsync(request, principalId, ResourceKinds.CustomPropertyProjection, true, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(RebuildEventCustomPropertyProjectionCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareResourceActionAsync(request, principalId, ResourceKinds.CustomPropertyProjection, true, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(RebuildSingleEventCustomPropertyProjectionCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareResourceActionAsync(request, principalId, ResourceKinds.CustomPropertyProjection, true, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(RebuildEventSessionCustomPropertyProjectionCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareResourceActionAsync(request, principalId, ResourceKinds.CustomPropertyProjection, true, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(RebuildSingleEventSessionCustomPropertyProjectionCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareResourceActionAsync(request, principalId, ResourceKinds.CustomPropertyProjection, true, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(UpdateLocationRoomCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareResourceActionAsync(request, principalId, ResourceKinds.LocationRoom, false, cancellationToken);

    private Task<IPreparedHumanAdministrativeAction> PrepareResourceActionAsync(
        object request, Guid principalId, string kind, bool revalidateResource, CancellationToken cancellationToken)
    {
        string originalContent = JsonSerializer.Serialize(request, request.GetType());
        void RequireContent()
        {
            if (!string.Equals(originalContent, JsonSerializer.Serialize(request, request.GetType()), StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The original administrative resource request changed.");
        }
        return PrepareAsync(request, principalId, instance: false, RequireContent, cancellationToken, kind, revalidateResource);
    }
}
