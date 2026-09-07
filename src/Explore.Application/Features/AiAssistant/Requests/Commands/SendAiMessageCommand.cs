using Explore.Application.Authorization;
using Explore.Application.DTOs.Ai;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Requests.Commands;

[AuthorizeResource(ResourceKinds.AiConversation, AuthorizationActions.AiConversations.SendMessage)]
public sealed record SendAiMessageCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid ConversationId { get; init; }
    public SendAiMessageRequestDto Message { get; init; } = new();

    string? ISecureRequest.ResourceId => ConversationId == Guid.Empty ? null : ConversationId.ToString();
}
