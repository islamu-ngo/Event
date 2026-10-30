using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.ServiceDefaults.Secrets;
using SecretKit.Core;
using SecretKit.Infisical;

namespace Explore.Infrastructure.Secrets;

/// <summary>
/// Borrows the selected authority for explicitly authorized, instance-only runtime
/// credential updates. Eligibility is neither provider permission nor activation.
/// </summary>
public sealed class AuthenticationRuntimeSecretWriter(
    ISecretBindingRepository bindings, SecretAuthorityAcquisition owner, ISecretResolver resolver)
    : IAuthenticationRuntimeSecretWriter
{
    public async Task<AuthenticationRuntimeWriteCapability> GetWriteCapabilityAsync(
        AuthenticationRuntimeSecretTarget target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecretDefinition? definition = Definition(target);
        if (definition is null
            || owner.Selection.Consumer is not (SecretConsumerRole.Api or SecretConsumerRole.Combined))
            return new(AuthenticationRuntimeWriteOutcome.Invalid);
        if (owner.Provider is SecretAuthorityKind.Environment or SecretAuthorityKind.UserSecrets)
            return new(AuthenticationRuntimeWriteOutcome.OperatorInjectionRequired,
                OperatorVariable: definition.DefaultEnvironmentVariableName);
        InfisicalAuthorityOptions? options = owner.InfisicalOptions;
        if (owner.Provider != SecretAuthorityKind.Infisical || options is null)
            return new(AuthenticationRuntimeWriteOutcome.Invalid);

        SecretBinding? stored = await bindings.GetByKeyAndScopeAsync(
            definition.Key, SecretScope.Instance, null, cancellationToken).ConfigureAwait(false);
        if (stored is null)
            return new(AuthenticationRuntimeWriteOutcome.Missing);
        SecretBinding binding = RuntimeSecretBinding.Capture(stored).ToBinding();
        if (binding.Id == Guid.Empty || binding.Scope != SecretScope.Instance || binding.ScopeId is not null
            || binding.Qualifier != string.Empty
            || !string.Equals(binding.SettingKey, definition.Key, StringComparison.Ordinal)
            || !SecretBindingAdmission.AllowsBinding(binding,
                [definition.DefaultInfisicalPath], options.VaultEnvironment))
            return new(AuthenticationRuntimeWriteOutcome.Invalid);
        try
        {
            using var operation = owner.BeginOperation(cancellationToken);
            if (owner.NativeClient is null)
                return new(AuthenticationRuntimeWriteOutcome.Unavailable);
        }
        catch (ObjectDisposedException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(AuthenticationRuntimeWriteOutcome.Unavailable);
        }
        return new(AuthenticationRuntimeWriteOutcome.ReadyToAttempt,
            new(target, binding.Id, binding.SourceType, binding.InfisicalEnvironment!,
                binding.InfisicalPath!, binding.InfisicalKey!, options.Origin.AbsoluteUri, options.ProjectId));
    }

    public async Task<AuthenticationRuntimeSecretWriteResult> WriteAsync(
        AuthenticationRuntimeSecretWriteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SetupSecretBindingWriter.IsValidUtf8(request.Credential, 16 * 1024))
            return new(AuthenticationRuntimeWriteOutcome.Invalid, Mutation(request.Attempt));
        AuthenticationRuntimeWriteCapability capability = await GetWriteCapabilityAsync(
            request.Selection.Target, cancellationToken).ConfigureAwait(false);
        if (capability.Outcome != AuthenticationRuntimeWriteOutcome.ReadyToAttempt)
            return new(capability.Outcome, Mutation(request.Attempt), capability.OperatorVariable);
        if (capability.Selection != request.Selection)
            return new(AuthenticationRuntimeWriteOutcome.Invalid, Mutation(request.Attempt));

        try
        {
            using var operation = owner.BeginOperation(cancellationToken);
            InfisicalClient? client = owner.NativeClient;
            InfisicalAuthorityOptions? options = owner.InfisicalOptions;
            if (client is null || options is null)
                return new(AuthenticationRuntimeWriteOutcome.Unavailable, Mutation(request.Attempt));
            var target = new SecretReference(client.Authority, new InfisicalSecretAddress(
                options.ProjectId, request.Selection.Environment, request.Selection.Path, request.Selection.Key,
                version: null, resolution: InfisicalResolution.Raw, includeImports: false));
            var receipt = new MutationReceipt(target);
            request.Attempt.AttachProjection(() => SetupSecretBindingWriter.Project(receipt));
            using SecretMaterial material = SetupSecretBindingWriter.ProtectUtf8(request.Credential);
            AuthenticationRuntimeWriteOutcome outcome = AuthenticationRuntimeWriteOutcome.Unavailable;
            bool invalidated = false;
            try
            {
                var result = await client.UpdateValueAsync(target, material, receipt, operation.Token)
                    .ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    outcome = result.Status switch
                    {
                        SecretStatus.Absent => AuthenticationRuntimeWriteOutcome.Missing,
                        SecretStatus.Denied => AuthenticationRuntimeWriteOutcome.Unauthorized,
                        SecretStatus.Invalid => AuthenticationRuntimeWriteOutcome.Invalid,
                        _ => AuthenticationRuntimeWriteOutcome.Unavailable
                    };
                }
            }
            finally
            {
                if (request.Attempt.Evidence == SecretMutationEvidence.Applied)
                {
                    await resolver.InvalidateAsync(Definition(request.Selection.Target)!.Key,
                        SecretScope.Instance, null, CancellationToken.None).ConfigureAwait(false);
                    invalidated = true;
                }
            }
            return new(request.Attempt.Evidence == SecretMutationEvidence.Applied && invalidated
                ? AuthenticationRuntimeWriteOutcome.Written : outcome, Mutation(request.Attempt));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(AuthenticationRuntimeWriteOutcome.Unavailable, Mutation(request.Attempt));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(AuthenticationRuntimeWriteOutcome.Unavailable, Mutation(request.Attempt));
        }
    }

    private static AuthenticationRuntimeSourceMutation Mutation(SecretMutationAttempt attempt) =>
        attempt.Evidence switch
        {
            SecretMutationEvidence.Applied => AuthenticationRuntimeSourceMutation.ConfirmedWritten,
            SecretMutationEvidence.NotDispatched => AuthenticationRuntimeSourceMutation.NotAttempted,
            _ => AuthenticationRuntimeSourceMutation.Unknown
        };

    private static SecretDefinition? Definition(AuthenticationRuntimeSecretTarget target) => target switch
    {
        AuthenticationRuntimeSecretTarget.GoogleClientSecret =>
            SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret),
        AuthenticationRuntimeSecretTarget.KeycloakBffClientSecret =>
            SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret),
        _ => null
    };
}
