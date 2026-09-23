using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Features.EventResources.Requests.Queries;
using Explore.Application.Services;

namespace Explore.Application.Features.EventResources.Handlers.Queries;

public sealed class AuthorizeEventResourceManagementDisclosureQueryHandler(EventResourceManagementWorkflow workflow)
    : IQueryHandler<AuthorizeEventResourceManagementDisclosureQuery, EventResourceAuthorityOutcome>
{
    public Task<EventResourceAuthorityOutcome> QueryAsync(AuthorizeEventResourceManagementDisclosureQuery query, CancellationToken cancellationToken = default) =>
        workflow.AuthorizeDisclosureAsync(query.CollectionEventId, query.Items, cancellationToken);
}

public sealed class GetEventResourceManagementDetailQueryHandler(EventResourceManagementWorkflow workflow)
    : IQueryHandler<GetEventResourceManagementDetailQuery, EventResourceManagementReadResult<EventResourceManagementDto>>
{
    public Task<EventResourceManagementReadResult<EventResourceManagementDto>> QueryAsync(GetEventResourceManagementDetailQuery query, CancellationToken cancellationToken = default) =>
        workflow.GetAsync(query.ResourceId, cancellationToken);
}

public sealed class ListEventResourceManagementQueryHandler(EventResourceManagementWorkflow workflow)
    : IQueryHandler<ListEventResourceManagementQuery, EventResourceManagementReadResult<EventResourceManagementPageDto>>
{
    public Task<EventResourceManagementReadResult<EventResourceManagementPageDto>> QueryAsync(ListEventResourceManagementQuery query, CancellationToken cancellationToken = default) =>
        workflow.ListAsync(query.EventId, query.Page, query.PageSize, cancellationToken);
}

public sealed class GetEventResourceAuditQueryHandler(EventResourceManagementWorkflow workflow)
    : IQueryHandler<GetEventResourceAuditQuery, EventResourceManagementReadResult<EventResourceAuditPageDto>>
{
    public Task<EventResourceManagementReadResult<EventResourceAuditPageDto>> QueryAsync(GetEventResourceAuditQuery query, CancellationToken cancellationToken = default) =>
        workflow.GetAuditAsync(query.ResourceId, query.Limit, cancellationToken);
}
