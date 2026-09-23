using Explore.Application.Contracts.Operations;
using Explore.Application.Features.EventResources.Requests.Queries;
using Explore.Application.Services;

namespace Explore.Application.Features.EventResources.Handlers.Queries;

public sealed class GetEventResourceAudienceDetailQueryHandler(EventResourceAudienceWorkflow workflow)
    : IQueryHandler<GetEventResourceAudienceDetailQuery, EventResourceAudienceDetailResult>
{
    public Task<EventResourceAudienceDetailResult> QueryAsync(GetEventResourceAudienceDetailQuery query,
        CancellationToken cancellationToken = default) => workflow.GetAsync(query.ResourceId, cancellationToken);
}

public sealed class ListEventResourcesQueryHandler(EventResourceAudienceWorkflow workflow)
    : IQueryHandler<ListEventResourcesQuery, EventResourceAudiencePageResult>
{
    public Task<EventResourceAudiencePageResult> QueryAsync(ListEventResourcesQuery query,
        CancellationToken cancellationToken = default) => workflow.ListAsync(query.EventId, query.PageSize, query.Cursor, cancellationToken);
}

public sealed class AuthorizeEventResourceAudienceDisclosureQueryHandler(EventResourceAudienceWorkflow workflow)
    : IQueryHandler<AuthorizeEventResourceAudienceDisclosureQuery, EventResourceAudienceFailure>
{
    public Task<EventResourceAudienceFailure> QueryAsync(AuthorizeEventResourceAudienceDisclosureQuery query,
        CancellationToken cancellationToken = default) => workflow.AuthorizeDisclosureAsync(query.Proof, cancellationToken);
}
