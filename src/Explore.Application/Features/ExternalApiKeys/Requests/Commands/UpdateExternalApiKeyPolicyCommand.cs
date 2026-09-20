using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Responses;

namespace Explore.Application.Features.ExternalApiKeys.Requests.Commands;

public sealed record UpdateExternalApiKeyPolicyCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid ExternalApiKeyId { get; init; }
    public required UpdateExternalApiKeyPolicyDto ExternalApiKeyPolicyDto { get; init; }
}
