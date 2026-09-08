namespace Explore.Application.Features.AiAssistant.Tools;

public sealed record AiToolResultPresentationMetadata(
    string CardKind,
    string ProposedTitle,
    string ConfirmedTitle,
    string FailedTitle);
