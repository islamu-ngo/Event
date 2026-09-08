using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Queries;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Queries;

public sealed class GetWebhookProviderPublicationsQueryHandler(
    IWebhookProviderPublicationRepository repository)
    : IRequestHandler<GetWebhookProviderPublicationsQuery, IReadOnlyList<WebhookProviderPublicationDto>>
{
    private const int DefaultLimit = 100;
    private const int MaximumLimit = 500;

    public async Task<IReadOnlyList<WebhookProviderPublicationDto>> Handle(
        GetWebhookProviderPublicationsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty ||
            request.StatusId is { } statusId &&
            !Enum.IsDefined(typeof(WebhookProviderPublicationStatus), statusId))
        {
            return [];
        }

        var limit = request.Limit <= 0 ? DefaultLimit : Math.Min(request.Limit, MaximumLimit);
        var publications = await repository.ListByTenantAsync(
            request.TenantId,
            Normalize(request.WebhookMessageId),
            Normalize(request.WebhookConsumerId),
            request.StatusId,
            limit,
            cancellationToken);
        return publications.Select(WebhookProviderPublicationDtoMapper.Map).ToArray();
    }

    private static Guid? Normalize(Guid? value) => value is { } id && id != Guid.Empty ? id : null;
}
