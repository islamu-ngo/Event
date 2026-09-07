using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Tools;

public interface IAiToolContractRegistry
{
    IReadOnlyList<AiToolDefinition> Definitions { get; }

    AiToolDefinition? FindDefinition(AiProposedActionKind kind);

    AiToolValidationResult ValidatePayload(
        AiProposedActionKind kind,
        string payloadJson,
        bool allowProviderNormalization = false);
}
