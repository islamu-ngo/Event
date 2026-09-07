using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Requests.Commands;

[AuthorizeResource(ResourceKinds.AiConversation, AuthorizationActions.AiConversations.ProposeAction)]
public sealed record ProposeAiToolActionCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid ConversationId { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public string PayloadJson { get; init; } = string.Empty;
    public string? Summary { get; init; }

    string? ISecureRequest.ResourceId => ConversationId == Guid.Empty ? null : ConversationId.ToString();
}
