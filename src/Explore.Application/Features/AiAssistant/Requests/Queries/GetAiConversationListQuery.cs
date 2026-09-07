using Explore.Application.Authorization;
using Explore.Application.DTOs.Ai;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Requests.Queries;

[AuthorizeResource(ResourceKinds.AiConversation, AuthorizationActions.AiConversations.View)]
public sealed record GetAiConversationListQuery : IRequest<IReadOnlyList<AiConversationSummaryDto>>
{
    public int Limit { get; init; } = 20;
}
