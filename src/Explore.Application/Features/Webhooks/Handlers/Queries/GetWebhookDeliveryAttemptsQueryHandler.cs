using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Queries;

public sealed class GetWebhookDeliveryAttemptsQueryHandler(
    IWebhookDeliveryAttemptRepository attemptRepository,
    IWebhookOwnershipScopeResolver ownershipScopeResolver)
    : IRequestHandler<GetWebhookDeliveryAttemptsQuery, IReadOnlyList<WebhookDeliveryAttemptDto>>
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    public async Task<IReadOnlyList<WebhookDeliveryAttemptDto>> Handle(
        GetWebhookDeliveryAttemptsQuery request,
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
        var messageId = request.MessageId is { } requestedMessageId && requestedMessageId != Guid.Empty
            ? requestedMessageId
            : (Guid?)null;
        var endpointId = request.EndpointId is { } requestedEndpointId && requestedEndpointId != Guid.Empty
            ? requestedEndpointId
            : (Guid?)null;

        var attempts = await attemptRepository.ListByOwnerAsync(
            ownership,
            messageId,
            endpointId,
            limit,
            cancellationToken);

        return attempts.Select(WebhookDeliveryAttemptDtoMapper.Map).ToList();
    }
}
