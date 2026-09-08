using Explore.Application.Authorization;
using Explore.Application.DTOs.Ai;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Requests.Commands;

[AuthorizeResource(ResourceKinds.AiConversation, AuthorizationActions.AiConversations.Create)]
public sealed record CreateAiConversationCommand : IRequest<BaseCommandResponse<Guid>>
{
    public CreateAiConversationRequestDto Conversation { get; init; } = new();
}
