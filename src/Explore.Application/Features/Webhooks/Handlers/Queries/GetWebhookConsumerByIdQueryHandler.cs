using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Queries;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Queries;

public sealed class GetWebhookConsumerByIdQueryHandler(
    IWebhookConsumerRepository consumerRepository,
    IWebhookConsumerProviderBindingRepository bindingRepository,
    IWebhookProviderCapabilityResolver capabilityResolver)
    : IRequestHandler<GetWebhookConsumerByIdQuery, WebhookConsumerDto?>
{
    public async Task<WebhookConsumerDto?> Handle(
        GetWebhookConsumerByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (request.ConsumerId == Guid.Empty)
        {
            return null;
        }

        var consumer = await consumerRepository.GetByIdForOwnerOperationAsync(
            request.ConsumerId,
            forUpdate: false,
            cancellationToken);

        if (consumer is null)
        {
            return null;
        }

        var resolution = capabilityResolver.Resolve(consumer.ProviderMode);
        WebhookConsumerProviderBinding? binding = null;
        if (consumer.ProviderMode is WebhookProviderMode.Svix or WebhookProviderMode.Composite &&
            !string.IsNullOrWhiteSpace(resolution.ProviderEnvironment))
        {
            binding = await bindingRepository.GetVerifiedByConsumerAsync(
                consumer.TenantId,
                request.ConsumerId,
                WebhookProviderKind.Svix,
                resolution.ProviderEnvironment,
                cancellationToken);
        }

        return WebhookConsumerDtoMapper.Map(consumer, resolution, binding);
    }
}
