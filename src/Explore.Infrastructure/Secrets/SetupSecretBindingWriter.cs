using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.SetupLive;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.ServiceDefaults.Secrets;
using SecretKit.Core;
using SecretKit.Infisical;

namespace Explore.Infrastructure.Secrets;

/// <summary>Updates only an existing, canonical setup binding through the selected authority.</summary>
public sealed class SetupSecretBindingWriter(
    ISecretBindingRepository bindings, SecretAuthorityAcquisition owner, ISecretResolver resolver)
    : ISetupSecretBindingWriter, ISetupSecretBindingReadinessReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Reports metadata eligibility only; Ready here is not mutation or delivery evidence.</summary>
    public async Task<SetupSecretBindingWriteOutcome> GetReadinessAsync(
        Guid bindingId, string bindingKey, CancellationToken cancellationToken)
    {
        var admission = await ResolveBindingAsync(bindingId, bindingKey, cancellationToken).ConfigureAwait(false);
        return admission.Outcome;
    }

    public async Task<SetupSecretBindingWriteOutcome> WriteAsync(
        SetupSecretBindingWriteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsValidUtf8(request.SecretValue, 65_536))
            return SetupSecretBindingWriteOutcome.Invalid;
        var admission = await ResolveBindingAsync(
            request.BindingId, request.BindingKey, cancellationToken).ConfigureAwait(false);
        if (admission.Outcome != SetupSecretBindingWriteOutcome.Ready)
            return admission.Outcome;
        SecretBinding binding = admission.Binding!;
        InfisicalAuthorityOptions options = owner.InfisicalOptions!;

        try
        {
            using var operation = owner.BeginOperation(cancellationToken);
            InfisicalClient? client = owner.NativeClient;
            if (client is null)
                return SetupSecretBindingWriteOutcome.Unavailable;
            var target = new SecretReference(client.Authority, new InfisicalSecretAddress(
                options.ProjectId, options.VaultEnvironment, binding.InfisicalPath!, binding.InfisicalKey!,
                version: null, resolution: InfisicalResolution.Raw, includeImports: false));
            var receipt = new MutationReceipt(target);
            request.Attempt.AttachProjection(() => Project(receipt));
            using SecretMaterial material = ProtectUtf8(request.SecretValue);
            SetupSecretBindingWriteOutcome outcome = SetupSecretBindingWriteOutcome.Unavailable;
            bool invalidated = false;
            try
            {
                var result = await client.UpdateValueAsync(target, material, receipt, operation.Token)
                    .ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    outcome = result.Status switch
                    {
                        SecretStatus.Denied => SetupSecretBindingWriteOutcome.Unauthorized,
                        SecretStatus.Invalid => SetupSecretBindingWriteOutcome.Invalid,
                        _ => SetupSecretBindingWriteOutcome.Unavailable
                    };
                }
            }
            finally
            {
                if (request.Attempt.Evidence == SecretMutationEvidence.Applied)
                {
                    // Cache eviction is required delivery work, not evidence of provider effect.
                    await resolver.InvalidateAsync(binding.SettingKey, SecretScope.Instance, null,
                        CancellationToken.None).ConfigureAwait(false);
                    invalidated = true;
                }
            }
            return request.Attempt.Evidence == SecretMutationEvidence.Applied && invalidated
                ? SetupSecretBindingWriteOutcome.Ready : outcome;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SetupSecretBindingWriteOutcome.Unavailable;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return SetupSecretBindingWriteOutcome.Unavailable;
        }
    }

    private async Task<(SecretBinding? Binding, SetupSecretBindingWriteOutcome Outcome)> ResolveBindingAsync(
        Guid bindingId, string bindingKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bindingKey is not (SecretDefinitionRegistry.Keys.SetupSigning
            or SecretDefinitionRegistry.Keys.SetupEncryption))
            return (null, SetupSecretBindingWriteOutcome.Invalid);
        SecretBinding? stored = await bindings.GetById(bindingId).ConfigureAwait(false);
        if (stored is null)
            return (null, SetupSecretBindingWriteOutcome.Invalid);
        SecretBinding binding = RuntimeSecretBinding.Capture(stored).ToBinding();
        if (binding.Id != bindingId || binding.Scope != SecretScope.Instance
            || binding.ScopeId is not null || binding.Qualifier != string.Empty
            || !string.Equals(binding.SettingKey, bindingKey, StringComparison.Ordinal))
            return (null, SetupSecretBindingWriteOutcome.Invalid);
        if (owner.Provider != SecretAuthorityKind.Infisical)
            return (null, SetupSecretBindingWriteOutcome.Unavailable);
        InfisicalAuthorityOptions? options = owner.InfisicalOptions;
        SecretDefinition definition = SecretDefinitionRegistry.GetRequired(bindingKey);
        if (options is null
            || !SecretBindingAdmission.AllowsBinding(binding,
                SecretAcquisitionPolicy.Roots(owner.Selection.Consumer), options.VaultEnvironment)
            || !string.Equals(binding.InfisicalKey, definition.DefaultInfisicalKey, StringComparison.Ordinal))
            return (null, SetupSecretBindingWriteOutcome.Invalid);
        try
        {
            using var operation = owner.BeginOperation(cancellationToken);
            return owner.NativeClient is null
                ? (null, SetupSecretBindingWriteOutcome.Unavailable)
                : (binding, SetupSecretBindingWriteOutcome.Ready);
        }
        catch (ObjectDisposedException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return (null, SetupSecretBindingWriteOutcome.Unavailable);
        }
    }

    internal static bool IsValidUtf8(ReadOnlyMemory<byte> input, int maximumBytes)
    {
        if (input.IsEmpty || input.Length > maximumBytes)
            return false;
        try
        {
            _ = StrictUtf8.GetCharCount(input.Span);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    internal static SecretMaterial ProtectUtf8(ReadOnlyMemory<byte> input)
    {
        char[] characters = new char[StrictUtf8.GetCharCount(input.Span)];
        try
        {
            StrictUtf8.GetChars(input.Span, characters);
            return SecretMaterial.FromText(characters);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(characters.AsSpan()));
        }
    }

    internal static SecretMutationEvidence Project(MutationReceipt receipt) => receipt.Evidence switch
    {
        DispatchEvidence.NotDispatched => SecretMutationEvidence.NotDispatched,
        DispatchEvidence.PossiblyDispatched => SecretMutationEvidence.PossiblyDispatched,
        DispatchEvidence.Applied => SecretMutationEvidence.Applied,
        DispatchEvidence.Pending => SecretMutationEvidence.Pending,
        DispatchEvidence.Rejected => SecretMutationEvidence.Rejected,
        DispatchEvidence.Conflict => SecretMutationEvidence.Conflict,
        _ => throw new InvalidOperationException("secret_mutation_evidence_invalid")
    };
}
