using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Queries;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Queries;

public sealed class GetWebhookConsumersQueryHandler(
    IWebhookConsumerRepository consumerRepository,
    IWebhookConsumerProviderBindingRepository bindingRepository,
    IWebhookProviderCapabilityResolver capabilityResolver,
    IWebhookOwnershipScopeResolver ownershipScopeResolver)
    : IRequestHandler<GetWebhookConsumersQuery, IReadOnlyList<WebhookConsumerDto>>
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    public async Task<IReadOnlyList<WebhookConsumerDto>> Handle(
        GetWebhookConsumersQuery request,
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

        var consumers = await consumerRepository.ListByOwnerAsync(
            ownership,
            limit,
            cancellationToken);

        var resolutions = consumers.ToDictionary(
            consumer => consumer.Id,
            consumer => capabilityResolver.Resolve(consumer.ProviderMode));
        var providerConsumerIds = consumers
            .Where(consumer => consumer.ProviderMode is WebhookProviderMode.Svix or WebhookProviderMode.Composite)
            .Select(consumer => consumer.Id)
            .ToArray();
        var providerEnvironment = resolutions.Values
            .Select(resolution => resolution.ProviderEnvironment)
            .FirstOrDefault(environment => !string.IsNullOrWhiteSpace(environment));
        IReadOnlyList<WebhookConsumerProviderBinding> bindings = providerConsumerIds.Length > 0 &&
            !string.IsNullOrWhiteSpace(providerEnvironment)
                ? await bindingRepository.GetVerifiedByConsumersAsync(
                    ownership.TenantId,
                    providerConsumerIds,
                    WebhookProviderKind.Svix,
                    providerEnvironment,
                    cancellationToken)
                : [];
        var bindingsByConsumer = bindings.ToDictionary(binding => binding.WebhookConsumerId);

        return consumers
            .Select(consumer => WebhookConsumerDtoMapper.Map(
                consumer,
                resolutions[consumer.Id],
                bindingsByConsumer.GetValueOrDefault(consumer.Id)))
            .ToList();
    }
}
