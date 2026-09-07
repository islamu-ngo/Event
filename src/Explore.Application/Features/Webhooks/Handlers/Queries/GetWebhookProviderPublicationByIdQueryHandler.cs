using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Queries;

public sealed class GetWebhookProviderPublicationByIdQueryHandler(
    IWebhookProviderPublicationRepository repository)
    : IRequestHandler<GetWebhookProviderPublicationByIdQuery, WebhookProviderPublicationDto?>
{
    public async Task<WebhookProviderPublicationDto?> Handle(
        GetWebhookProviderPublicationByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.PublicationId == Guid.Empty)
        {
            return null;
        }

        var publication = await repository.GetByTenantAndIdAsync(
            request.TenantId,
            request.PublicationId,
            cancellationToken);
        return publication is null ? null : WebhookProviderPublicationDtoMapper.Map(publication);
    }
}
