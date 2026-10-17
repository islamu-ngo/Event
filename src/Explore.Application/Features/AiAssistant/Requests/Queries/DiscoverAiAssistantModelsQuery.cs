using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Ai;

namespace Explore.Application.Features.AiAssistant.Requests.Queries;

public sealed record DiscoverAiAssistantModelsQuery(AiAssistantModelDiscoveryRequestDto Request)
    : IQuery<AiModelDiscoveryResult>;

public sealed record AiModelDiscoveryResult
{
    public AiModelDiscoveryResult(IReadOnlyList<AiAssistantModelDto> models, string? failureCode = null)
    {
        Models = Array.AsReadOnly(models.ToArray());
        FailureCode = failureCode;
    }

    public IReadOnlyList<AiAssistantModelDto> Models { get; }
    public string? FailureCode { get; }
}

public interface IAiModelDiscoveryGateway
{
    Task<AiModelDiscoveryResult> DiscoverAsync(
        AiAssistantModelDiscoveryRequestDto request, CancellationToken cancellationToken);
}
