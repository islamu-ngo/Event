using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Domain.Ai;

namespace Explore.Application.Contracts.Persistence;

/// <summary>One missing-target HeavyModerateEvent proposal under its original conversation owner.</summary>
public interface IHumanAiModerationProposalPreparation
{
    Task<IPreparedHumanAiModerationProposal> PrepareAsync(
        ProposeAiToolActionCommand request, AiConversation candidate, CancellationToken cancellationToken);
}

public interface IPreparedHumanAiModerationProposal
{
    Task AcquireAsync(ProposeAiToolActionCommand originalRequest, CancellationToken cancellationToken);
    void RetainProposal(AiConversation conversation, AiProposedAction proposedAction);
}

