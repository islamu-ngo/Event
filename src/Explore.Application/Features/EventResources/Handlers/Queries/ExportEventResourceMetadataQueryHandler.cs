using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Features.EventResources.Requests.Queries;
using Explore.Application.Services;

namespace Explore.Application.Features.EventResources.Handlers.Queries;

public sealed class ExportEventResourceMetadataQueryHandler(EventResourceManagementWorkflow workflow)
    : IQueryHandler<ExportEventResourceMetadataQuery, EventResourceManagementReadResult<EventResourceMetadataExportPageDto>>
{
    public Task<EventResourceManagementReadResult<EventResourceMetadataExportPageDto>> QueryAsync(
        ExportEventResourceMetadataQuery query, CancellationToken cancellationToken = default) =>
        workflow.ExportAsync(query.EventId, query.Page, query.PageSize, cancellationToken);
}
