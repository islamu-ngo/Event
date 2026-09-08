using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Actions;

public sealed class CreateEventDraftAiToolExecutor(IMediator mediator)
{
    private readonly CreateEventDraftAiActionMapper _mapper = new();

    public Task<CreateEventDraftAiToolExecutionResult> ExecuteAsync(
        string payloadJson,
        CreateEventDraftAiActionMappingContext? mappingContext,
        CancellationToken cancellationToken)
        => ExecuteAsync(payloadJson, mappingContext, null, cancellationToken);

    public async Task<CreateEventDraftAiToolExecutionResult> ExecuteAsync(
        string payloadJson,
        CreateEventDraftAiActionMappingContext? mappingContext,
        Func<CancellationToken, Task<CreateEventDraftAiFeaturedImageResolutionResult>>? featuredImageResolver,
        CancellationToken cancellationToken)
    {
        var mappingResult = _mapper.Map(payloadJson, mappingContext);
        if (!mappingResult.Succeeded)
        {
            return CreateEventDraftAiToolExecutionResult.Failure(
                mappingResult.FailureCode ?? "invalid_tool_arguments",
                mappingResult.FailureMessage ?? "AI event draft payload could not be mapped.");
        }

        var draft = mappingResult.Draft!;
        if (draft.FeaturedImageId is null && featuredImageResolver is not null)
        {
            var featuredImageResult = await featuredImageResolver(cancellationToken);
            if (!featuredImageResult.Succeeded)
            {
                return CreateEventDraftAiToolExecutionResult.Failure(
                    featuredImageResult.FailureCode ?? "ai_image_upload_failed",
                    featuredImageResult.FailureMessage ?? "AI event draft image could not be stored.");
            }

            draft.FeaturedImageId = featuredImageResult.FeaturedImageId;
        }

        BaseCommandResponse<Guid> createResult = await mediator.Send(new CreateEventCommand
        {
            EventDto = draft.ToCreateEventDto()
        }, cancellationToken);

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
