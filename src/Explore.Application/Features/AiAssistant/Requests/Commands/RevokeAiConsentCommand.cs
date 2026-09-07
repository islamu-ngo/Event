using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Requests.Commands;

public sealed record RevokeAiConsentCommand : IRequest<BaseCommandResponse<Guid>>
{
    public required Guid GrantId { get; init; }
    public required Guid RevokedByUserId { get; init; }
}
