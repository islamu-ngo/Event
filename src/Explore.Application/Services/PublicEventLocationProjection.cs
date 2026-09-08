using System.Collections.Immutable;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Location;

namespace Explore.Application.Services;

internal readonly record struct PublicEventLocationPlacement(
    Guid TenantId,
    Guid EventId,
    Guid? EventLocationId,
    Guid? RoomId);

internal static class PublicEventLocationProjection
{
    public static async Task<IReadOnlyDictionary<Guid, EventLocationPublicDto>> ResolveAsync(
        IEventLocationDisclosureService disclosureService,
        IEnumerable<PublicEventLocationPlacement> placements,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(disclosureService);
        ArgumentNullException.ThrowIfNull(placements);

        EventLocationDisclosureRequest[] requests = placements
            .Where(placement => placement.EventLocationId.HasValue)
            .Select(placement => new EventLocationDisclosureRequest(
                placement.TenantId,
                placement.EventId,
                placement.EventLocationId!.Value,
                placement.RoomId,
                RequesterUserId: null,
                EventLocationDisclosurePurpose.Public))
            .ToArray();
        if (requests.Length == 0)
        {
            return ImmutableDictionary<Guid, EventLocationPublicDto>.Empty;
        }

        IReadOnlyDictionary<Guid, EventLocationDisclosureResult> results =
            await disclosureService.ResolveManyAsync(requests, cancellationToken);
        return results.ToImmutableDictionary(
            pair => pair.Key,
            pair => EventLocationPublicDto.FromDisclosureResult(pair.Value));
    }
}
