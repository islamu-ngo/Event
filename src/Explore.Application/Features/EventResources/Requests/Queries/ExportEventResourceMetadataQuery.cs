using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Services;

namespace Explore.Application.Features.EventResources.Requests.Queries;

/// <summary>Handler-owned current parent and per-resource export authority.</summary>
public sealed record ExportEventResourceMetadataQuery(Guid EventId, int Page = 1, int PageSize = 20)
    : IQuery<EventResourceManagementReadResult<EventResourceMetadataExportPageDto>>;
