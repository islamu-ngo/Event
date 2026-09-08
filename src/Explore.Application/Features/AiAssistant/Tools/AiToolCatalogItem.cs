using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Tools;

public sealed record AiToolCatalogItem(
    AiProposedActionKind Kind,
    string Name,
    string DisplayName,
    AiToolAgentMetadata Metadata,
    bool CanRequestProposal,
    bool ExecutionAuthorityGranted,
    string AvailabilityCode,
    string AvailabilityReason);
