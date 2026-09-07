using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Contracts.Providers;

public interface IRuntimeRenderPolicyService
{
    Task<RuntimeRenderPolicyDecision> ResolveForPathAsync(string? rawPath, CancellationToken cancellationToken = default);
}
