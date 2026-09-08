using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Queries;

public sealed class GetWebhookEndpointsQueryHandler(
    IWebhookEndpointRepository endpointRepository,
    IWebhookOwnershipScopeResolver ownershipScopeResolver)
    : IRequestHandler<GetWebhookEndpointsQuery, IReadOnlyList<WebhookEndpointDto>>
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    public async Task<IReadOnlyList<WebhookEndpointDto>> Handle(
        GetWebhookEndpointsQuery request,
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

        var endpoints = await endpointRepository.ListByOwnerAsync(
            ownership,
            request.ConsumerId,
            limit,
            cancellationToken);

        return endpoints
            .Select(WebhookEndpointDtoMapper.Map)
            .ToArray();
    }
}
