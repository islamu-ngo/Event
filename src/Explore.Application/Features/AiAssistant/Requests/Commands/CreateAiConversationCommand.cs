using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Ai;
using Explore.Application.Responses;

namespace Explore.Application.Features.AiAssistant.Requests.Commands;

[AuthorizeResource(ResourceKinds.AiConversation, AuthorizationActions.AiConversations.Create)]
public sealed record CreateAiConversationCommand : ICommand<BaseCommandResponse<Guid>>
{
    public CreateAiConversationRequestDto Conversation { get; init; } = new();
}
