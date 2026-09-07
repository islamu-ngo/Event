namespace Explore.Application.Contracts.Infrastructure.Ai;

public interface IAiProviderStrategyResolver
{
    IAiProviderStrategy? Resolve(int providerId);
}
