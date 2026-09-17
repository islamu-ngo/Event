using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.PrivacyErasure;
using Explore.Application.Features.PrivacyErasure.Requests.Queries;

namespace Explore.Application.Features.PrivacyErasure.Handlers.Queries;

public sealed class GetPrivacyErasureStatusQueryHandler(IPrivacyErasureService service)
    : IQueryHandler<GetPrivacyErasureStatusQuery, PrivacyErasureStatusDto?>
{
    public Task<PrivacyErasureStatusDto?> QueryAsync(
        GetPrivacyErasureStatusQuery request,
        CancellationToken cancellationToken) =>
        service.GetStatusAsync(request.IntentId, cancellationToken);
}
