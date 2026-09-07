using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Ai;
using Explore.Application.Features.AiAssistant.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Handlers.Queries;

public sealed class GetAiRunStatusQueryHandler : IRequestHandler<GetAiRunStatusQuery, AiRunDto?>
{
    private readonly IAiConversationRepository _conversationRepository;
    private readonly ICurrentUserService _currentUserService;

    public GetAiRunStatusQueryHandler(
        IAiConversationRepository conversationRepository,
        ICurrentUserService currentUserService)
    {
        _conversationRepository = conversationRepository;
        _currentUserService = currentUserService;
    }

    public async Task<AiRunDto?> Handle(GetAiRunStatusQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId is not { } userId)
        {
            return null;
        }

        var conversation = await _conversationRepository.GetByIdWithDetailsAsync(
            request.ConversationId, cancellationToken);

        if (conversation is null || conversation.UserId != userId)
        {
            return null;
        }

        var run = conversation.Runs.FirstOrDefault(candidate => candidate.Id == request.RunId);
        return run is null ? null : AiAssistantConversationMapper.ToRun(run);
    }
}
