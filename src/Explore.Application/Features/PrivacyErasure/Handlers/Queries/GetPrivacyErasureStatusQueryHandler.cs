using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.PrivacyErasure;
using Explore.Application.Features.PrivacyErasure.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.PrivacyErasure.Handlers.Queries;

public sealed class GetPrivacyErasureStatusQueryHandler(IPrivacyErasureService service)
    : IRequestHandler<GetPrivacyErasureStatusQuery, PrivacyErasureStatusDto?>
{
    public Task<PrivacyErasureStatusDto?> Handle(
        GetPrivacyErasureStatusQuery request,
        CancellationToken cancellationToken) =>
        service.GetStatusAsync(request.IntentId, cancellationToken);
}
