using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Requests.Commands;

[AuthorizeResource(ResourceKinds.AiConversation, AuthorizationActions.AiConversations.ConfirmAction)]
public sealed record ConfirmAiProposedActionCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid ProposedActionId { get; init; }

    public string? IdempotencyKey { get; init; }

    string? ISecureRequest.ResourceId => ProposedActionId == Guid.Empty ? null : ProposedActionId.ToString();
}
