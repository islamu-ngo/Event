using System.Collections.Immutable;
using Explore.Application.Authorization;
using Explore.Application.Services;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventResource;

namespace Explore.Application.Features.EventResources.Requests.Queries;

/// <summary>Rechecks prepared metadata after asynchronous affordance assembly, before HTTP disclosure.</summary>
public sealed record AuthorizeEventResourceManagementDisclosureQuery(
    Guid? CollectionEventId, ImmutableArray<EventResourceManagementDto> Items)
    : IQuery<EventResourceAuthorityOutcome>;

[AuthorizeResource(ResourceKinds.EventResource, "view-management")]
public sealed record GetEventResourceManagementDetailQuery(Guid ResourceId)
    : IQuery<EventResourceManagementReadResult<EventResourceManagementDto>>, ISecureRequest
{
    string ISecureRequest.ResourceId => ResourceId.ToString("D");
}

/// <summary>Handler-owned exact parent-event authority; no fabricated resource or create-capacity gate.</summary>
public sealed record ListEventResourceManagementQuery(Guid EventId, int Page = 1, int PageSize = 20)
    : IQuery<EventResourceManagementReadResult<EventResourceManagementPageDto>>;

[AuthorizeResource(ResourceKinds.EventResource, "view-audit")]
public sealed record GetEventResourceAuditQuery(Guid ResourceId, int Limit = 100)
    : IQuery<EventResourceManagementReadResult<EventResourceAuditPageDto>>, ISecureRequest
{
    string ISecureRequest.ResourceId => ResourceId.ToString("D");
}
