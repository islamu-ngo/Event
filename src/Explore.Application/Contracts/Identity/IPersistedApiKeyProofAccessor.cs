using Explore.Application.Authentication;

namespace Explore.Application.Contracts.Identity;

/// <summary>Reads authentication-owned server evidence, never machine claims.</summary>
public interface IPersistedApiKeyProofAccessor
{
    PersistedApiKeyAuthenticationProof? CurrentProof { get; }
}
