using Explore.Application.Contracts.Operations;
using Explore.Application.Services;

namespace Explore.Application.Features.EventResources.Requests.Queries;

public sealed record GetEventResourceAudienceDetailQuery(Guid ResourceId)
    : IQuery<EventResourceAudienceDetailResult>;

public sealed record ListEventResourcesQuery(Guid EventId, int PageSize = 20, string? Cursor = null)
    : IQuery<EventResourceAudiencePageResult>;

/// <summary>Reauthorizes the exact prepared disclosure after asynchronous HAL assembly.</summary>
public sealed record AuthorizeEventResourceAudienceDisclosureQuery(EventResourceAudienceDisclosureProof Proof)
    : IQuery<EventResourceAudienceFailure>;
