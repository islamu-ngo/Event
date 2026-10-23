using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Actions;

public sealed class CreateEventDraftAiToolExecutor(ICommandHandler<CreateEventCommand, BaseCommandResponse<Guid>> createCommandHandler)
{
    private readonly CreateEventDraftAiActionMapper _mapper = new();

    public async Task<CreateEventDraftAiToolExecutionResult> ExecuteAsync(
        AiToolExecution execution,
        string payloadJson,
        CreateEventDraftAiActionMappingContext? mappingContext,
        CancellationToken cancellationToken)
    {
        var mappingResult = _mapper.Map(payloadJson, mappingContext);
        if (!mappingResult.Succeeded)
        {
            return CreateEventDraftAiToolExecutionResult.Failure(
                mappingResult.FailureCode ?? "invalid_tool_arguments",
                mappingResult.FailureMessage ?? "AI event draft payload could not be mapped.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        BaseCommandResponse<Guid> createResult = await createCommandHandler.ExecuteAsync(
            CreateEventCommand.FromAiDraft(execution, payloadJson, mappingContext), cancellationToken);

        if (!createResult.IsSuccess || createResult.Id == Guid.Empty)
        {
            return CreateEventDraftAiToolExecutionResult.Failure(
                createResult.FailureCode ?? "event_creation_failed",
                createResult.Message ?? "AI event draft confirmation could not create an event.");
        }

        return CreateEventDraftAiToolExecutionResult.Success(createResult.Id);
    }
}

public sealed record CreateEventDraftAiToolExecutionResult(
    bool Succeeded,
    Guid? ResultResourceId,
    string? FailureCode,
    string? FailureMessage)
{
    public static CreateEventDraftAiToolExecutionResult Success(Guid resultResourceId)
        => new(true, resultResourceId, null, null);

    public static CreateEventDraftAiToolExecutionResult Failure(string failureCode, string failureMessage)
        => new(false, null, failureCode, failureMessage);
}

public sealed record CreateEventDraftAiFeaturedImageResolutionResult(
    bool Succeeded,
    Guid? FeaturedImageId,
    string? FailureCode,
    string? FailureMessage)
{
    public static CreateEventDraftAiFeaturedImageResolutionResult Success(Guid? featuredImageId)
        => new(true, featuredImageId, null, null);

    public static CreateEventDraftAiFeaturedImageResolutionResult Failure(string failureCode, string failureMessage)
        => new(false, null, failureCode, failureMessage);
}
