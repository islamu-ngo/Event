namespace Explore.Application.Features.AiAssistant.Actions;

internal sealed record ActorMappingContextResult(
    bool Succeeded,
    CreateEventDraftAiActionMappingContext? Context,
    string? FailureCode,
    string? FailureMessage)
{
    public static ActorMappingContextResult Success(CreateEventDraftAiActionMappingContext context)
        => new(true, context, null, null);

    public static ActorMappingContextResult Failure(string failureCode, string failureMessage)
        => new(false, null, failureCode, failureMessage);
}
