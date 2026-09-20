using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventTicketing;
using Explore.Application.Features.EventTicketing.Requests.Queries;
using Explore.Application.Features.EventTicketing.Services;

namespace Explore.Application.Features.EventTicketing.Handlers.Queries;

public sealed class GetPaidEventPublicationPreflightQueryHandler(PaidEventPublicationPreflightService preflight)
    : IQueryHandler<GetPaidEventPublicationPreflightQuery, PaidEventPublicationPreflightDto>
{
    public Task<PaidEventPublicationPreflightDto> QueryAsync(
        GetPaidEventPublicationPreflightQuery query,
        CancellationToken cancellationToken) => preflight.AssessAsync(query.EventId, cancellationToken);
}
