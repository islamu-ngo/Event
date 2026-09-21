using Explore.Domain;

namespace Explore.Application.Contracts.Services;

/// <summary>Reads the durable setup generation without resolving secrets or verifying external services.</summary>
public interface IInstanceOnboardingGenerationReader
{
    Task<string> ReadAsync(InstanceBootstrapState? bootstrap, CancellationToken cancellationToken);
}
