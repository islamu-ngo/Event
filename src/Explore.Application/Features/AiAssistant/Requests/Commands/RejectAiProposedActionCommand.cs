using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Requests.Commands;

[AuthorizeResource(ResourceKinds.AiConversation, AuthorizationActions.AiConversations.RejectAction)]
public sealed record RejectAiProposedActionCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid ProposedActionId { get; init; }

    string? ISecureRequest.ResourceId => ProposedActionId == Guid.Empty ? null : ProposedActionId.ToString();
}
