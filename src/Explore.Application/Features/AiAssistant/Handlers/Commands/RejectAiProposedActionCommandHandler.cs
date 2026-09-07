using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Ai;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Handlers.Commands;

public sealed class RejectAiProposedActionCommandHandler(
    IAiConversationRepository conversationRepository,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService) : IRequestHandler<RejectAiProposedActionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        RejectAiProposedActionCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.UserId is not { } userId)
        {
            return Failure("AI proposed action rejection requires an authenticated user.", ["User is not authenticated."], "unauthenticated");
        }

        AiProposedAction? action = await conversationRepository.GetProposedActionForUpdateAsync(request.ProposedActionId, cancellationToken);
        if (!IsActionVisibleToCurrentPrincipal(action, tenantContext.TenantId, userId))
        {
            return Failure("AI proposed action was not found.", ["Proposed action was not found."], "proposed_action_not_found");
        }

        if (action!.Status == AiProposedActionStatus.Rejected)
        {
            return Success(action.Id, "AI proposed action was already rejected.");
        }

        if (action.Status != AiProposedActionStatus.Proposed)
        {
            return Failure("AI proposed action cannot be rejected in its current state.", ["Only proposed actions can be rejected."], "invalid_proposed_action_state", action.Id);
        }

        action.Reject(userId, DateTime.UtcNow);
        await conversationRepository.UpdateProposedActionAsync(action, cancellationToken);

        return Success(action.Id, "AI proposed action rejected.");
    }

    private static bool IsActionVisibleToCurrentPrincipal(AiProposedAction? action, Guid tenantId, Guid userId)
        => action?.Conversation is not null
            && action.TenantId == tenantId
            && action.Conversation.TenantId == tenantId
            && action.Conversation.UserId == userId;

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(
        string message,
        IEnumerable<string> errors,
        string? failureCode = null,
        Guid id = default) => failureCode is null
            ? BaseCommandResponse.Validation<Guid>(errors, message, id)
            : BaseCommandResponse.Failure<Guid>(failureCode, message, errors, id);
}
