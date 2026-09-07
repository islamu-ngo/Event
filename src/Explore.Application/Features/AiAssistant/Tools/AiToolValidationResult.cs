namespace Explore.Application.Features.AiAssistant.Tools;

public sealed record AiToolValidationResult(
    bool Succeeded,
    string? FailureCode,
    string? FailureMessage,
    string? CorrectionMessage = null,
    AiToolRecoveryResult? Recovery = null,
    string? NormalizedPayloadJson = null)
{
    public AiToolRecoveryResult EffectiveRecovery => Recovery ?? AiToolRecoveryResult.None;

    public static AiToolValidationResult Success(string? normalizedPayloadJson = null)
        => new(true, null, null, null, AiToolRecoveryResult.None, normalizedPayloadJson);

    public static AiToolValidationResult Failure(string failureCode, string failureMessage, string? correctionMessage = null)
        => new(
            false,
            failureCode,
            failureMessage,
            correctionMessage,
            AiToolRecoveryResult.ForFailure(failureCode, correctionMessage));

    public static AiToolValidationResult ClarificationFailure(
        string failureCode,
        string failureMessage,
        string clarificationQuestion,
        string? correctionMessage = null)
        => new(
            false,
            failureCode,
            failureMessage,
            correctionMessage,
            AiToolRecoveryResult.ForClarification(failureCode, clarificationQuestion, correctionMessage));
}
