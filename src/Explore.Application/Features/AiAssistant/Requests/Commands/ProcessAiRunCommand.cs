using Explore.Application.DTOs.Ai;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Requests.Commands;

public sealed record ProcessAiRunCommand : IRequest
{
    public Guid TenantId { get; init; }
    public Guid ConversationId { get; init; }
    public Guid RunId { get; init; }
    public string Mode { get; init; } = AiAssistantInteractionModes.Build;
}
