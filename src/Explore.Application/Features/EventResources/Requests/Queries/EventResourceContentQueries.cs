using Explore.Application.Contracts.Operations;
using Explore.Application.Services;

namespace Explore.Application.Features.EventResources.Requests.Queries;

public sealed record GetEventResourceContentQuery(Guid ResourceId, DateTimeOffset DeadlineUtc)
    : IQuery<EventResourceAuthorityResult>
{
    public override string ToString() => nameof(GetEventResourceContentQuery);
}

public sealed record CompleteEventResourceContentQuery(EventResourceAuthorityLease Lease)
    : IQuery<EventResourceHeaderResult>;
