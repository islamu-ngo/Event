namespace Explore.Application.Features.AiAssistant.Rag;

public sealed record AiRagIngestionValidationResult(
    bool Succeeded,
    string? FailureCode,
    string? FailureMessage)
{
    public static AiRagIngestionValidationResult Success() => new(true, null, null);

    public static AiRagIngestionValidationResult Failure(string failureCode, string failureMessage)
        => new(false, failureCode, failureMessage);
}
