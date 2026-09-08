using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Ai;
using Explore.Application.Features.AiAssistant.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Handlers.Queries;

public sealed class GetAiConversationDetailQueryHandler
    : IRequestHandler<GetAiConversationDetailQuery, AiConversationDto?>
{
    private readonly IAiConversationRepository _conversationRepository;
    private readonly ICurrentUserService _currentUserService;

    public GetAiConversationDetailQueryHandler(
        IAiConversationRepository conversationRepository,
        ICurrentUserService currentUserService)
    {
        _conversationRepository = conversationRepository;
        _currentUserService = currentUserService;
    }

    public async Task<AiConversationDto?> Handle(
        GetAiConversationDetailQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId is not { } userId)
        {
            return null;
        }

        var conversation = await _conversationRepository.GetByIdWithDetailsAsync(
            request.ConversationId, cancellationToken);

        return conversation is null || conversation.UserId != userId
            ? null
            : AiAssistantConversationMapper.ToDetail(conversation);
    }
}
