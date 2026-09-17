using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.AiAssistant.Requests.Commands;

[AuthorizeResource(ResourceKinds.AiConversation, AuthorizationActions.AiConversations.RejectAction)]
public sealed record RejectAiProposedActionCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid ProposedActionId { get; init; }

    string? ISecureRequest.ResourceId => ProposedActionId == Guid.Empty ? null : ProposedActionId.ToString();
}
