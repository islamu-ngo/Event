using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Ai;

namespace Explore.Application.Features.AiAssistant.Requests.Queries;

[AuthorizeResource(ResourceKinds.AiConversation, AuthorizationActions.AiConversations.View)]
public sealed record GetAiConversationListQuery : IQuery<IReadOnlyList<AiConversationSummaryDto>>
{
    public int Limit { get; init; } = 20;
}
