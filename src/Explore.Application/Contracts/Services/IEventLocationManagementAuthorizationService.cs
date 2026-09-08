using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Services;

public interface IEventLocationManagementAuthorizationService
{
    const int MaximumBatchSize = 256;

    Task<IReadOnlyDictionary<Guid, bool>> AuthorizeManyAsync(
        IReadOnlyCollection<EventLocation> eventLocations,
        EventLocationExactReadPurposeEnum purpose,
        Guid? correlationId,
        Guid? traceId,
        CancellationToken cancellationToken);
}
