using Explore.Application.Settings.Groups;

namespace Explore.Application.Contracts.Infrastructure.Ai;

public interface IAiProviderCapabilityResolver
{
    Task<AiProviderCapability> ResolveAsync(
        AiAssistantSettingGroup settings, CancellationToken cancellationToken = default);
}

public sealed record AiProviderCapability(bool Available, string? DisabledReason);
