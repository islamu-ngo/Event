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
    /// Performs a live round-trip against the declared source to confirm the reference is valid and the
    /// credential produces a non-empty value. Used by the admin "Validate" action.
    /// </summary>
    Task<bool> ValidateAsync(SecretBinding binding, CancellationToken cancellationToken = default);
}
