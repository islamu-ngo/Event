using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.ExternalApiKeys.Requests.Commands;

public sealed record UpdateExternalApiKeyPolicyCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid ExternalApiKeyId { get; init; }
    public required UpdateExternalApiKeyPolicyDto ExternalApiKeyPolicyDto { get; init; }
}
