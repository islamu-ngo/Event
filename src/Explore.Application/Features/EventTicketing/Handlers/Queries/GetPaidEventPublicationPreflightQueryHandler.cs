using Explore.Application.DTOs.EventTicketing;
using Explore.Application.Features.EventTicketing.Requests.Queries;
using Explore.Application.Features.EventTicketing.Services;
using MediatR;

namespace Explore.Application.Features.EventTicketing.Handlers.Queries;

public sealed class GetPaidEventPublicationPreflightQueryHandler(PaidEventPublicationPreflightService preflight)
    : IRequestHandler<GetPaidEventPublicationPreflightQuery, PaidEventPublicationPreflightDto>
{
    public Task<PaidEventPublicationPreflightDto> Handle(
        GetPaidEventPublicationPreflightQuery request,
        CancellationToken cancellationToken) => preflight.AssessAsync(request.EventId, cancellationToken);
}
