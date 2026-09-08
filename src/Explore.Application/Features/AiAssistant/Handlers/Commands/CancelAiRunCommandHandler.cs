using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Ai;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Handlers.Commands;

public sealed class CancelAiRunCommandHandler(
    IAiConversationRepository conversationRepository,
    ICurrentUserService currentUserService)
    : IRequestHandler<CancelAiRunCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        CancelAiRunCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.UserId is not { } userId)
        {
            return Failure("AI run cancellation requires an authenticated user.", "unauthenticated");
        }

        var conversation = await conversationRepository.GetByIdForUpdateAsync(request.ConversationId, cancellationToken);

        if (conversation is null || conversation.UserId != userId)
        {
            return Failure("AI run was not found.", "run_not_found");
        }

        var run = conversation.Runs.FirstOrDefault(candidate => candidate.Id == request.RunId);

        if (run is null)
        {
            return Failure("AI run was not found.", "run_not_found");
        }

        if (run.Status == AiRunStatus.Cancelled)
        {
            return Success(run.Id, "AI run was already cancelled.");
        }

        if (run.Status is AiRunStatus.Succeeded or AiRunStatus.Failed)
        {
            return Failure("Completed AI runs cannot be cancelled.", "run_not_cancellable", run.Id);
        }

        conversation.CancelRun(run, DateTime.UtcNow);
        await conversationRepository.Update(conversation);

        return Success(run.Id, "AI run cancelled.");
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(string message, string failureCode, Guid? id = null) =>
        BaseCommandResponse.Failure<Guid>(failureCode, message, [message], id ?? Guid.Empty);
}
