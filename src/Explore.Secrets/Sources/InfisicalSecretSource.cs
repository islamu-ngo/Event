using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Microsoft.Extensions.Logging;

namespace Explore.Secrets.Sources;

/// <summary>
/// Retrieves secrets from Infisical via the <see cref="IInfisicalClientFactory"/>. When the factory
/// returns <c>null</c> (Infisical not configured) or the client returns no value, the source
/// returns an unconfigured outcome. Provider failures become bounded unavailable/unauthorized outcomes.
/// </summary>
public sealed class InfisicalSecretSource : ISecretSource
{
    private readonly IInfisicalClientFactory _clientFactory;
    private readonly ILogger<InfisicalSecretSource> _logger;

    public InfisicalSecretSource(IInfisicalClientFactory clientFactory, ILogger<InfisicalSecretSource> logger)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _clientFactory = clientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public SecretSourceType SourceType => SecretSourceType.Infisical;

    /// <inheritdoc />
    public async Task<SecretResolutionResult> GetSecretAsync(
        SecretBinding binding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (string.IsNullOrWhiteSpace(binding.InfisicalEnvironment)
            || string.IsNullOrWhiteSpace(binding.InfisicalPath)
            || string.IsNullOrWhiteSpace(binding.InfisicalKey))
        {
            _logger.LogWarning("secret_source_invalid");
            return SecretResolutionResult.Invalid;
        }

        var client = await _clientFactory.GetClientAsync(cancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            _logger.LogWarning("secret_source_unconfigured");
            return SecretResolutionResult.Unconfigured;
        }

        try
        {
            var value = await client.GetSecretAsync(
                binding.InfisicalEnvironment,
                binding.InfisicalPath,
                binding.InfisicalKey,
                cancellationToken).ConfigureAwait(false);

            return string.IsNullOrEmpty(value)
                ? SecretResolutionResult.Unconfigured
                : SecretResolutionResult.Resolved(new ResolvedSecret(
                    binding.SettingKey,
                    value,
                    binding.SourceType,
                    binding.Scope,
                    binding.ScopeId,
                    DateTime.UtcNow));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning("secret_source_unauthorized");
            return SecretResolutionResult.Unauthorized;
        }
#pragma warning disable CA1031 // Provider boundary translates diagnostics to a bounded status.
        catch (Exception)
#pragma warning restore CA1031
        {
            _logger.LogError("secret_source_unavailable");
            return SecretResolutionResult.Unavailable;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ValidateAsync(SecretBinding binding, CancellationToken cancellationToken = default)
    {
        var result = await GetSecretAsync(binding, cancellationToken).ConfigureAwait(false);
        return result.IsResolved;
    }
}
