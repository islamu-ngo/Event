using Explore.Application.DTOs.Onboarding;
using Explore.Domain;

namespace Explore.Application.Contracts.Services;

/// <summary>Reads the durable setup generation without resolving secrets or verifying external services.</summary>
public interface IInstanceOnboardingGenerationReader
{
    Task<string> ReadAsync(InstanceBootstrapState? bootstrap, CancellationToken cancellationToken);
    Task<string> ReadCurrentAsync(CancellationToken cancellationToken);
    Task<InstanceOnboardingDurableSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken);
}

/// <summary>The generation and profile projected from the same durable settings read.</summary>
public sealed record InstanceOnboardingDurableSnapshot(string Generation, SelfHostOnboardingProfileDto Profile)
{
    public override string ToString() => nameof(InstanceOnboardingDurableSnapshot);
}
