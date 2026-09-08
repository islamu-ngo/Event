using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Queries;

public sealed class GetWebhookDeliveryAttemptByIdQueryHandler(IWebhookDeliveryAttemptRepository attemptRepository)
    : IRequestHandler<GetWebhookDeliveryAttemptByIdQuery, WebhookDeliveryAttemptDto?>
{
    public async Task<WebhookDeliveryAttemptDto?> Handle(
        GetWebhookDeliveryAttemptByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (request.AttemptId == Guid.Empty)
        {
            return null;
        }

        var attempt = await attemptRepository.GetByIdForOwnerOperationAsync(
            request.AttemptId,
            cancellationToken);

        return attempt is null ? null : WebhookDeliveryAttemptDtoMapper.Map(attempt);
    }
}
