using Explore.Application.Contracts.Operations;
using Explore.Application.Services;

namespace Explore.Application.Features.EventResources.Requests.Queries;

public sealed record GetEventResourceAccessQuery(Guid ResourceId, DateTimeOffset DeadlineUtc)
    : IQuery<EventResourceAuthorityResult>
{
    public override string ToString() => nameof(GetEventResourceAccessQuery);
}

public sealed record GetEventResourceAccessHeadersQuery(EventResourceAuthorityLease Lease)
    : IQuery<EventResourceHeaderResult>;
