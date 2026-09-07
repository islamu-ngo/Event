using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Queries;

public sealed class GetWebhookBulkReplayOperationQueryHandler(IWebhookBulkReplayRepository repository)
    : IRequestHandler<GetWebhookBulkReplayOperationQuery, WebhookBulkReplayOperationDto?>
{
    public async Task<WebhookBulkReplayOperationDto?> Handle(
        GetWebhookBulkReplayOperationQuery request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.OperationId == Guid.Empty)
        {
            return null;
        }

        var operation = await repository.GetByTenantAndIdAsync(
            request.TenantId,
            request.OperationId,
            cancellationToken);
        return operation is null ? null : WebhookBulkReplayDtoMapper.Map(operation);
    }
}
