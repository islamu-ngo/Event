using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Queries;

public sealed class GetWebhookMessagesQueryHandler(
    IWebhookMessageRepository messageRepository,
    IWebhookOwnershipScopeResolver ownershipScopeResolver)
    : IRequestHandler<GetWebhookMessagesQuery, IReadOnlyList<WebhookMessageDto>>
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    public async Task<IReadOnlyList<WebhookMessageDto>> Handle(
        GetWebhookMessagesQuery request,
        CancellationToken cancellationToken)
    {
        var ownershipResolution = await ownershipScopeResolver.ResolveAsync(
            request.OwnerKindId,
            request.OwnerId,
            cancellationToken);
        if (ownershipResolution.Scope is not { } ownership)
        {
            return [];
        }

        var limit = request.Limit <= 0
            ? DefaultLimit
            : Math.Min(request.Limit, MaxLimit);

        var messages = await messageRepository.ListByOwnerAsync(
            ownership,
            limit,
            cancellationToken);

        return messages.Select(WebhookMessageDtoMapper.Map).ToList();
    }
}
