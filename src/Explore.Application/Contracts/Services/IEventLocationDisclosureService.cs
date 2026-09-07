using Explore.Application.Contracts.LocationPrivacy;

namespace Explore.Application.Contracts.Services;

public interface IEventLocationDisclosureService
{
    const int MaximumBatchSize = 256;

    Task<IReadOnlyDictionary<Guid, EventLocationDisclosureResult>> ResolveManyAsync(
        IReadOnlyCollection<EventLocationDisclosureRequest> requests,
        CancellationToken cancellationToken);
}
