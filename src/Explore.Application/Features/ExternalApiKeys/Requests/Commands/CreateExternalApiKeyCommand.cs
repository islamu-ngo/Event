using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.ExternalApiKeys.Requests.Commands;

public sealed record CreateExternalApiKeyCommand : IRequest<CreateExternalApiKeyCommandResponse>
{
    public required CreateExternalApiKeyDto ExternalApiKeyDto { get; init; }
}
