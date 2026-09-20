using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.AiAssistant.Requests.Commands;

public sealed record RevokeAiConsentCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required Guid GrantId { get; init; }
    public required Guid RevokedByUserId { get; init; }
}
