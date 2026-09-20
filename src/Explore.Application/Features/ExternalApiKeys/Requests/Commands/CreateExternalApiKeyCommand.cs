using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Responses;

namespace Explore.Application.Features.ExternalApiKeys.Requests.Commands;

public sealed record CreateExternalApiKeyCommand : ICommand<CreateExternalApiKeyCommandResponse>
{
    public required CreateExternalApiKeyDto ExternalApiKeyDto { get; init; }
}
