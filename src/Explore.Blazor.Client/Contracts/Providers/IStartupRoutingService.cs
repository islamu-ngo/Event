using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Contracts.Providers;

public interface IStartupRoutingService
{
    Task<StartupRouteDecision> GetRootDecisionAsync();
}
