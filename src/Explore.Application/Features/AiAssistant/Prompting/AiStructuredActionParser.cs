using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Prompting;

public sealed class AiStructuredActionParser
{
    private readonly IAiToolContractRegistry _toolRegistry;

    public AiStructuredActionParser()
        : this(AiToolContractRegistry.CreateDefault())
    {
    }

    public AiStructuredActionParser(IAiToolContractRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry;
    }

    public AiStructuredActionParseResult Parse(IReadOnlyList<AiProposedActionCandidate> candidates)
    {
        var actions = new List<AiParsedProposedAction>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var definition = _toolRegistry.FindDefinition(candidate.Kind);
            if (definition is null || !definition.ExposeToProvider)
            {
                return AiStructuredActionParseResult.Failure(
                    "unknown_action_kind",
                    "AI provider returned an unsupported proposed action kind.",
                    AiToolCorrectionMessages.SchemaExactRetry);
            }

            var validationResult = _toolRegistry.ValidatePayload(
                candidate.Kind,
                candidate.PayloadJson,
                allowProviderNormalization: true);
            if (!validationResult.Succeeded)
            {
                return AiStructuredActionParseResult.Failure(
                    validationResult.FailureCode ?? "invalid_tool_arguments",
                    validationResult.FailureMessage ?? "AI provider returned invalid action payload JSON.",
                    validationResult.CorrectionMessage);
            }

            actions.Add(new AiParsedProposedAction(
                candidate.Kind,
                validationResult.NormalizedPayloadJson ?? candidate.PayloadJson,
                candidate.Summary));
        }

        return AiStructuredActionParseResult.Success(actions);
    }
}

public sealed record AiParsedProposedAction(
    AiProposedActionKind Kind,
    string PayloadJson,
    string? Summary);

public sealed record AiStructuredActionParseResult(
    bool Succeeded,
    IReadOnlyList<AiParsedProposedAction> Actions,
    string? FailureCode,
    string? FailureMessage,
    string? CorrectionMessage = null)
{
    public static AiStructuredActionParseResult Success(IReadOnlyList<AiParsedProposedAction> actions)
        => new(true, actions, null, null, null);

    public static AiStructuredActionParseResult Failure(
        string failureCode,
        string failureMessage,
        string? correctionMessage = null)
        => new(false, [], failureCode, failureMessage, correctionMessage);
}
