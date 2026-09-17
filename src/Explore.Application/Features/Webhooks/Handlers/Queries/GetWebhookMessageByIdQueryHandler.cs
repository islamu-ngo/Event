using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Queries;

namespace Explore.Application.Features.Webhooks.Handlers.Queries;

public sealed class GetWebhookMessageByIdQueryHandler(IWebhookMessageRepository messageRepository)
    : IQueryHandler<GetWebhookMessageByIdQuery, WebhookMessageDto?>
{
    public async Task<WebhookMessageDto?> QueryAsync(
        GetWebhookMessageByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (request.MessageId == Guid.Empty)
        {
            return null;
        }

        var message = await messageRepository.GetByIdForOwnerOperationAsync(
            request.MessageId,
            cancellationToken);

        return message is null ? null : WebhookMessageDtoMapper.Map(message);
    }
}
