using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.Secrets.Sources;

/// <summary>
/// Reads the binding's <see cref="SecretBinding.EnvironmentVariableName"/> from the explicitly selected
/// local authority. The persisted source type stays EnvironmentVariable because User Secrets is a
/// development transport, not a new deployment-owned binding model.
/// </summary>
public sealed class EnvironmentSecretSource(
    IOptions<SecretProviderOptions> options,
    UserSecretsAuthority userSecretsAuthority) : ISecretSource
{
    /// <inheritdoc />
    public SecretSourceType SourceType => SecretSourceType.EnvironmentVariable;

    /// <inheritdoc />
    public Task<SecretResolutionResult> GetSecretAsync(
        SecretBinding binding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (string.IsNullOrWhiteSpace(binding.EnvironmentVariableName))
        {
            return Task.FromResult(SecretResolutionResult.Invalid);
        }

        var value = options.Value.Provider == SecretProviderType.UserSecrets
            ? userSecretsAuthority.Get(binding.EnvironmentVariableName)
            : Environment.GetEnvironmentVariable(binding.EnvironmentVariableName);
        if (string.IsNullOrEmpty(value))
        {
            return Task.FromResult(SecretResolutionResult.Unconfigured);
        }

        return Task.FromResult(SecretResolutionResult.Resolved(new ResolvedSecret(
            binding.SettingKey,
            value,
            binding.SourceType,
            binding.Scope,
            binding.ScopeId,
            DateTime.UtcNow)));
    }

    /// <inheritdoc />
    public async Task<bool> ValidateAsync(SecretBinding binding, CancellationToken cancellationToken = default)
    {
        var result = await GetSecretAsync(binding, cancellationToken).ConfigureAwait(false);
        return result.IsResolved;
    }
}
