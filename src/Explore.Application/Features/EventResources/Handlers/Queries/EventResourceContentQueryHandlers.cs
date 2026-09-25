using Explore.Application.Contracts.Operations;
using Explore.Application.Features.EventResources.Requests.Queries;
using Explore.Application.Services;

namespace Explore.Application.Features.EventResources.Handlers.Queries;

public sealed class GetEventResourceContentQueryHandler(EventResourceContentService content)
    : IQueryHandler<GetEventResourceContentQuery, EventResourceAuthorityResult>
{
    public Task<EventResourceAuthorityResult> QueryAsync(GetEventResourceContentQuery query,
        CancellationToken cancellationToken = default) =>
        content.PrepareAsync(query.ResourceId, query.DeadlineUtc, cancellationToken);
}

public sealed class GetEventResourceContentHeadersQueryHandler(EventResourceAuthorityOrchestrator authority)
    : IQueryHandler<GetEventResourceContentHeadersQuery, EventResourceHeaderResult>
{
    public Task<EventResourceHeaderResult> QueryAsync(GetEventResourceContentHeadersQuery query,
        CancellationToken cancellationToken = default) =>
        authority.CompleteHeadersAsync(query.Lease, cancellationToken);
}
