using Explore.Application.Contracts.Infrastructure.Ai;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Ai;

public sealed class AiProviderStrategyResolver : IAiProviderStrategyResolver
{
    private readonly IEnumerable<IAiProviderStrategy> _strategies;
    private readonly ILogger<AiProviderStrategyResolver> _logger;

    public AiProviderStrategyResolver(
        IEnumerable<IAiProviderStrategy> strategies,
        ILogger<AiProviderStrategyResolver> logger)
    {
        _strategies = strategies;
        _logger = logger;
    }

    public IAiProviderStrategy? Resolve(int providerId)
    {
        if (providerId <= 0)
            return null;

        var strategy = _strategies.FirstOrDefault(s => s.SupportsProvider(providerId));

        if (strategy is null)
        {
            _logger.LogDebug("No AI provider strategy found for provider ID '{ProviderId}'", providerId);
        }

        return strategy;
    }
}
