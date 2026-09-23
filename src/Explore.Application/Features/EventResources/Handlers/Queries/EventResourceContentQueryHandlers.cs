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

public sealed class CompleteEventResourceContentQueryHandler(EventResourceAuthorityOrchestrator authority)
    : IQueryHandler<CompleteEventResourceContentQuery, EventResourceHeaderResult>
{
    public Task<EventResourceHeaderResult> QueryAsync(CompleteEventResourceContentQuery query,
        CancellationToken cancellationToken = default) =>
        authority.CompleteHeadersAsync(query.Lease, cancellationToken);
}
