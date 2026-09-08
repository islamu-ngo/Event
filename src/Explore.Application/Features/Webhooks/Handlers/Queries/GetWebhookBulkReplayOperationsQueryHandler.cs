using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Queries;

public sealed class GetWebhookBulkReplayOperationsQueryHandler(IWebhookBulkReplayRepository repository)
    : IRequestHandler<GetWebhookBulkReplayOperationsQuery, IReadOnlyList<WebhookBulkReplayOperationDto>>
{
    private const int MaximumLimit = 500;

    public async Task<IReadOnlyList<WebhookBulkReplayOperationDto>> Handle(
        GetWebhookBulkReplayOperationsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty)
        {
            return [];
        }

        var limit = Math.Clamp(request.Limit, 1, MaximumLimit);
        var operations = await repository.ListByTenantAsync(request.TenantId, limit, cancellationToken);
        return operations.Select(WebhookBulkReplayDtoMapper.Map).ToArray();
    }
}
