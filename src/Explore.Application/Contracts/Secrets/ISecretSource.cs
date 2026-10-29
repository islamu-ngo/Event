using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Explore.Application.Contracts.Secrets;

/// <summary>
/// Retrieves secret values from a single declared source. The resolver selects one
/// <see cref="ISecretSource"/> by <see cref="SourceType"/>
/// matching the binding's declared source — there is no source-level fallback chain.
/// </summary>
public interface ISecretSource
{
    /// <summary>Which <see cref="SecretSourceType"/> this source handles. Exactly one source per type.</summary>
    SecretSourceType SourceType { get; }

    /// <summary>
    /// Retrieves the plaintext secret described by <paramref name="binding"/> as a bounded typed outcome.
    /// Implementations preserve cancellation and translate provider failures without exposing diagnostics.
    /// </summary>
    Task<SecretResolutionResult> GetSecretAsync(
        SecretBinding binding,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms the reference produces a non-empty value within the selected authority.
    /// A source may validate its process-frozen generation rather than fresh ambient configuration
    /// or a remote round-trip. Used by the admin "Validate" action.
    /// </summary>
    Task<bool> ValidateAsync(SecretBinding binding, CancellationToken cancellationToken = default);
}
