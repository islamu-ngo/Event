using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Ai;

namespace Explore.Application.Features.AiAssistant.Requests.Commands;

public sealed record ProcessAiRunCommand : ICommand
{
    public Guid TenantId { get; init; }
    public Guid ConversationId { get; init; }
    public Guid RunId { get; init; }
    public string Mode { get; init; } = AiAssistantInteractionModes.Build;
}
