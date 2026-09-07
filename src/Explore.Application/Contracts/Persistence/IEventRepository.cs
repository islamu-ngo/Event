using Explore.Application.Specifications.Events;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventRepository : IGenericRepository<Event, Guid>
{
    const int MaximumAuthorizationTargetBatchSize = 256;

    Task<Event?> GetEventWithDetails(Guid id);
    Task<Event?> GetEventWithDetailsAsync(
        Guid id,
        Guid tenantId,
        CancellationToken cancellationToken);
    Task<Event?> GetPublicEventWithDetailsByCodeAsync(string publicCode, CancellationToken cancellationToken);
    Task<Event?> GetPublicEventForOpenGraphAsync(string publicCode, CancellationToken cancellationToken);
    Task<bool> IsPubliclyEligibleAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken);
    Task<Event?> GetScheduleGraphForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<Event?> GetAuthorizationTargetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Event>> GetAuthorizationTargetsByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);
    Task<AtprotoEventPublicationEntityGraph?> GetAtprotoPublicationGraphAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken);
    Task<Event?> GetAtprotoLifecycleStateAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken);
    Task<List<Event>> GetEventsWithDetails();
    Task<List<Event>> GetMyEventsWithDetails(string userId);
    Task<IReadOnlyList<Event>> GetEventsByActorWithDetails(Guid actorId, CancellationToken cancellationToken = default);


    /// <summary>
    /// Gets a paginated list of events with details.
    /// </summary>
    /// <param name="pageNumber">The page number (1-based).</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <returns>A tuple containing the items and total count.</returns>
    Task<(List<Event> Items, int TotalCount)> GetEventsWithDetailsPaged(int pageNumber, int pageSize);

    /// <summary>
    /// Gets a paginated and filtered list of events with details.
    /// </summary>
    /// <param name="pageNumber">The page number (1-based).</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <param name="specification">The query specification containing filters and sort criteria.</param>
    /// <returns>A tuple containing the items and total count.</returns>
    Task<(List<Event> Items, int TotalCount)> GetEventsWithDetailsPaged(
        int pageNumber, int pageSize, EventQuerySpecification specification);

    /// <summary>
    /// Gets public, published events eligible for sitemap generation.
    /// </summary>
    /// <param name="maxCount">The maximum number of events to return. Sitemap protocol cap is 50,000.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Published public events for the current tenant.</returns>
    Task<List<Event>> GetPublishedPublicEventsForSitemap(int maxCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches public event references that are safe to expose as lightweight AI context.
    /// </summary>
    /// <param name="searchTerm">Trimmed search term.</param>
    /// <param name="limit">Maximum number of event entities to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tenant-filtered event entities for application-layer reference mapping.</returns>
    Task<IReadOnlyList<Event>> SearchAiReferenceEventsAsync(
        string searchTerm,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets a paginated list of events for the current user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="pageNumber">The page number (1-based).</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <returns>A tuple containing the items and total count.</returns>
    Task<(List<Event> Items, int TotalCount)> GetMyEventsWithDetailsPaged(string userId, int pageNumber, int pageSize);
}
