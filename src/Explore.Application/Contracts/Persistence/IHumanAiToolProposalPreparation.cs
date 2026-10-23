using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Domain.Ai;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Prepares an ordinary proposal's selected-provider question before its single Serializable attempt.
/// The original human and selected tenant remain authoritative; this does not authorize execution.
/// </summary>
public interface IHumanAiToolProposalPreparation
{
    Task<IPreparedHumanAiToolProposal> PrepareAsync(
        ProposeAiToolActionCommand request, AiConversation candidate, CancellationToken cancellationToken);
}

public interface IPreparedHumanAiToolProposal
{
    Task AcquireAsync(ProposeAiToolActionCommand originalRequest, CancellationToken cancellationToken);
    void RetainProposal(AiConversation conversation, AiProposedAction proposedAction);
}
