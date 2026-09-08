using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Queries;

public sealed class GetWebhookEndpointByIdQueryHandler(
    IWebhookEndpointRepository endpointRepository)
    : IRequestHandler<GetWebhookEndpointByIdQuery, WebhookEndpointDto?>
{
    public async Task<WebhookEndpointDto?> Handle(
        GetWebhookEndpointByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (request.EndpointId == Guid.Empty)
        {
            return null;
        }

        var endpoint = await endpointRepository.GetByIdForOwnerOperationAsync(
            request.EndpointId,
            forUpdate: false,
            cancellationToken);

        return endpoint is null
            ? null
            : WebhookEndpointDtoMapper.Map(endpoint);
    }
}
