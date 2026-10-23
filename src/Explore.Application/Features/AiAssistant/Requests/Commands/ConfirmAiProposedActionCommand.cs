using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.AiAssistant.Requests.Commands;

[AuthorizeResource(ResourceKinds.AiConversation, AuthorizationActions.AiConversations.ConfirmAction)]
public sealed record ConfirmAiProposedActionCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid ConversationId { get; init; }
    public Guid ProposedActionId { get; init; }
    public string ExpectedVersion { get; init; } = string.Empty;

    public string? IdempotencyKey { get; init; }

    string? ISecureRequest.ResourceId => ConversationId == Guid.Empty ? null : ConversationId.ToString();
}
