using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Ai;
using Explore.Application.Features.AiAssistant.Requests.Queries;

namespace Explore.Application.Features.AiAssistant.Handlers.Queries;

public sealed class GetAiConversationListQueryHandler
    : IQueryHandler<GetAiConversationListQuery, IReadOnlyList<AiConversationSummaryDto>>
{
    private readonly IAiConversationRepository _conversationRepository;
    private readonly ICurrentUserService _currentUserService;

    public GetAiConversationListQueryHandler(
        IAiConversationRepository conversationRepository,
        ICurrentUserService currentUserService)
    {
        _conversationRepository = conversationRepository;
        _currentUserService = currentUserService;
    }

    public async Task<IReadOnlyList<AiConversationSummaryDto>> QueryAsync(
        GetAiConversationListQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId is not { } userId)
        {
            return [];
        }

        var conversations = await _conversationRepository.ListRecentForUserAsync(
            userId, request.Limit, cancellationToken);

        return conversations.Select(AiAssistantConversationMapper.ToSummary).ToList();
    }
}
