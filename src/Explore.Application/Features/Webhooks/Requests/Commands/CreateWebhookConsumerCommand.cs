using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Commands;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.Create)]
public sealed record CreateWebhookConsumerCommand
    : IRequest<BaseCommandResponse<Guid>>, ISecureRequest, IWebhookOwnerScopedRequest
{
    public Guid? OwnerId { get; init; }

    public int ConsumerKindId { get; init; }

    public string Name { get; init; } = string.Empty;

    public int ProviderModeId { get; init; }

    int IWebhookOwnerScopedRequest.OwnerKindId => ConsumerKindId;

    string? ISecureRequest.ResourceId => OwnerId?.ToString("D");

}
