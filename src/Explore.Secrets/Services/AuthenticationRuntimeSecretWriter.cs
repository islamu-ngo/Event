using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Explore.Secrets.Services;

/// <summary>
/// Instance-only selected-authority dispatch for separately authorized credential operations.
/// Eligibility never certifies remote permission; source acknowledgment never certifies activation.
/// </summary>
public sealed class AuthenticationRuntimeSecretWriter(
    ISecretBindingRepository bindings, IInfisicalClientFactory clients, ISecretResolver resolver,
    IOptions<SecretProviderOptions> options, IHostEnvironment hostEnvironment)
    : IAuthenticationRuntimeSecretWriter
{
    private const int MaximumCredentialBytes = 16 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<AuthenticationRuntimeWriteCapability> GetWriteCapabilityAsync(
        AuthenticationRuntimeSecretTarget target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecretDefinition? definition = Definition(target);
        if (definition is null)
            return new(AuthenticationRuntimeWriteOutcome.Invalid);
        SecretProviderOptions selected = options.Value;
        if (selected.Provider == SecretProviderType.Environment ||
            (selected.Provider == SecretProviderType.UserSecrets &&
                SecretAuthorityConfiguration.IsDevelopmentOrTesting(hostEnvironment.EnvironmentName)))
            return new(AuthenticationRuntimeWriteOutcome.OperatorInjectionRequired,
                OperatorVariable: definition.DefaultEnvironmentVariableName);
        if (selected.Provider != SecretProviderType.Infisical)
            return new(AuthenticationRuntimeWriteOutcome.Invalid);

        SecretBinding? binding = await bindings.GetByKeyAndScopeAsync(
            definition.Key, SecretScope.Instance, null, cancellationToken);
        if (binding is null)
            return new(AuthenticationRuntimeWriteOutcome.Missing);
        if (binding.Scope != SecretScope.Instance || binding.ScopeId is not null ||
            !string.IsNullOrEmpty(binding.Qualifier) ||
            binding.SourceType != SecretSourceType.Infisical ||
            string.IsNullOrWhiteSpace(selected.Infisical.Environment) ||
            !string.Equals(binding.InfisicalEnvironment, selected.Infisical.Environment, StringComparison.Ordinal) ||
            !string.Equals(binding.InfisicalPath, definition.DefaultInfisicalPath, StringComparison.Ordinal) ||
            !string.Equals(binding.InfisicalKey, definition.DefaultInfisicalKey, StringComparison.Ordinal))
            return new(AuthenticationRuntimeWriteOutcome.Invalid);
        try
        {
            if (await clients.GetClientAsync(cancellationToken) is null)
                return new(AuthenticationRuntimeWriteOutcome.Missing);
        }
        catch (OperationCanceledException) { throw; }
        catch (InfisicalOperationException failure) { return new(Outcome(failure.FailureKind)); }
        catch (Exception) { return new(AuthenticationRuntimeWriteOutcome.Unavailable); }

        return new(AuthenticationRuntimeWriteOutcome.ReadyToAttempt,
            new(target, binding.Id, binding.SourceType, binding.InfisicalEnvironment!,
                binding.InfisicalPath!, binding.InfisicalKey!, selected.Infisical.Url, selected.Infisical.ProjectId));
    }

    public async Task<AuthenticationRuntimeSecretWriteResult> WriteAsync(
        AuthenticationRuntimeSecretWriteRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Credential.IsEmpty || request.Credential.Length > MaximumCredentialBytes)
            return new(AuthenticationRuntimeWriteOutcome.Invalid, AuthenticationRuntimeSourceMutation.NotAttempted);
        try { _ = StrictUtf8.GetCharCount(request.Credential.Span); }
        catch (DecoderFallbackException)
        {
            return new(AuthenticationRuntimeWriteOutcome.Invalid, AuthenticationRuntimeSourceMutation.NotAttempted);
        }

        AuthenticationRuntimeWriteCapability capability = await GetWriteCapabilityAsync(
            request.Selection.Target, cancellationToken);
        if (capability.Outcome != AuthenticationRuntimeWriteOutcome.ReadyToAttempt)
            return new(capability.Outcome, AuthenticationRuntimeSourceMutation.NotAttempted, capability.OperatorVariable);
        if (capability.Selection != request.Selection)
            return new(AuthenticationRuntimeWriteOutcome.Invalid, AuthenticationRuntimeSourceMutation.NotAttempted);

        IInfisicalClient? client;
        try { client = await clients.GetClientAsync(cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (InfisicalOperationException failure)
        {
            return new(Outcome(failure.FailureKind), AuthenticationRuntimeSourceMutation.NotAttempted);
        }
        catch (Exception)
        {
            return new(AuthenticationRuntimeWriteOutcome.Unavailable, AuthenticationRuntimeSourceMutation.NotAttempted);
        }
        if (client is null)
            return new(AuthenticationRuntimeWriteOutcome.Missing, AuthenticationRuntimeSourceMutation.NotAttempted);
        cancellationToken.ThrowIfCancellationRequested();
        bool written;
        try
        {
            written = await client.WriteSecretAsync(request.Selection.Environment,
                request.Selection.Path, request.Selection.Key, request.Credential, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (InfisicalOperationException failure)
        {
            return new(Outcome(failure.FailureKind), failure.DefiniteRejection
                ? AuthenticationRuntimeSourceMutation.NotAttempted : AuthenticationRuntimeSourceMutation.Unknown);
        }
        catch (Exception)
        {
            return new(AuthenticationRuntimeWriteOutcome.Unavailable, AuthenticationRuntimeSourceMutation.Unknown);
        }
        if (!written)
            return new(AuthenticationRuntimeWriteOutcome.Unavailable, AuthenticationRuntimeSourceMutation.Unknown);

        // A later failure cannot undo or justify repeating the acknowledged source update.
        try
        {
            await resolver.InvalidateAsync(Definition(request.Selection.Target)!.Key,
                SecretScope.Instance, null, CancellationToken.None);
        }
        catch (Exception)
        {
            return new(AuthenticationRuntimeWriteOutcome.Unavailable, AuthenticationRuntimeSourceMutation.ConfirmedWritten);
        }
        return new(AuthenticationRuntimeWriteOutcome.Written, AuthenticationRuntimeSourceMutation.ConfirmedWritten);
    }

    private static SecretDefinition? Definition(AuthenticationRuntimeSecretTarget target) => target switch
    {
        AuthenticationRuntimeSecretTarget.GoogleClientSecret =>
            SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret),
        AuthenticationRuntimeSecretTarget.KeycloakBffClientSecret =>
            SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret),
        _ => null
    };

    private static AuthenticationRuntimeWriteOutcome Outcome(InfisicalOperationFailureKind kind) => kind switch
    {
        InfisicalOperationFailureKind.Missing => AuthenticationRuntimeWriteOutcome.Missing,
        InfisicalOperationFailureKind.Invalid => AuthenticationRuntimeWriteOutcome.Invalid,
        InfisicalOperationFailureKind.Unauthorized => AuthenticationRuntimeWriteOutcome.Unauthorized,
        InfisicalOperationFailureKind.Unavailable => AuthenticationRuntimeWriteOutcome.Unavailable,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
