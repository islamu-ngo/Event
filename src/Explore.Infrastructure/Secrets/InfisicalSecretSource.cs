using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.ServiceDefaults.Secrets;
using SecretKit.Core;
using SecretKit.Infisical;

namespace Explore.Infrastructure.Secrets;

/// <summary>
/// Borrows the selected native client for raw, direct reads. Only qualified native
/// revision equality can establish an owner-bound provisioning comparison token.
/// </summary>
public sealed class InfisicalSecretSource : ISecretSource, ISecretRevisionOwner
{
    private readonly SecretAuthorityAcquisition _authority;

    public InfisicalSecretSource(SecretAuthorityAcquisition authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        _authority = authority;
    }

    public SecretSourceType SourceType => SecretSourceType.Infisical;

    public bool IsRevisionCurrent(SecretBinding binding, string? revision)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return _authority.Provider == SecretAuthorityKind.Infisical && binding.SourceType == SourceType
            && SecretBindingRevisionInventory.IsCurrent(_authority, RuntimeSecretBinding.Capture(binding), revision);
    }

    public async Task<SecretResolutionResult> GetSecretAsync(
        SecretBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();
        RuntimeSecretBinding metadata = RuntimeSecretBinding.Capture(binding);
        binding = metadata.ToBinding();
        InfisicalAuthorityOptions? options = _authority.InfisicalOptions;
        if (_authority.Provider != SecretAuthorityKind.Infisical || options is null
            || binding.SourceType != SourceType
            || !SecretBindingAdmission.AllowsBinding(binding,
                SecretAcquisitionPolicy.Roots(_authority.Selection.Consumer), options.VaultEnvironment))
            return SecretResolutionResult.Invalid;

        try
        {
            using var bounded = _authority.BeginOperation(cancellationToken);
            InfisicalClient? client = _authority.NativeClient;
            if (client is null)
                return SecretResolutionResult.Unavailable;
            var reference = new SecretReference(client.Authority, new InfisicalSecretAddress(
                options.ProjectId, binding.InfisicalEnvironment!, binding.InfisicalPath!, binding.InfisicalKey!,
                version: null, resolution: InfisicalResolution.Raw, includeImports: false));
            var observation = SecretBindingRevisionInventory.BeginObservation(_authority, metadata);
            var result = await client.ReadNativeAsync(reference, bounded.Token).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                bounded.Token.ThrowIfCancellationRequested();
                return Failure(result.Status);
            }
            using var read = result.Value;
            bounded.Token.ThrowIfCancellationRequested();
            _ = _authority.NativeClient;
            if (!read.Observation.Reference.BelongsTo(client.Authority)
                || read.Observation.Reference.Address is not InfisicalSecretAddress address
                || !string.Equals(address.ProjectId, options.ProjectId, StringComparison.Ordinal)
                || !string.Equals(address.Environment, binding.InfisicalEnvironment, StringComparison.Ordinal)
                || !string.Equals(address.Folder, binding.InfisicalPath, StringComparison.Ordinal)
                || !string.Equals(address.Key, binding.InfisicalKey, StringComparison.Ordinal)
                || address.Resolution != InfisicalResolution.Raw || address.IncludeImports
                || read.Observation.Validity != SecretValidity.Valid)
                return SecretResolutionResult.Invalid;
            if (read.Revision is null)
                return SecretResolutionResult.Invalid;
            string value = read.Material.Reveal();
            if (value.Length == 0)
                return SecretResolutionResult.Unconfigured;
            string? revision = observation.Complete(read.Revision);
            if (revision is null)
                return SecretResolutionResult.Unavailable;
            if (!SecretBindingRevisionInventory.IsCurrent(_authority, metadata, revision))
                return SecretResolutionResult.Unavailable;
            bounded.Token.ThrowIfCancellationRequested();
            return SecretResolutionResult.Resolved(new(
                binding.SettingKey, value, binding.SourceType, binding.Scope, binding.ScopeId,
                read.Observation.ObservedAt, revision));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SecretResolutionResult.Unavailable;
        }
        catch (ObjectDisposedException)
        {
            SecretBindingRevisionInventory.Forget(_authority);
            cancellationToken.ThrowIfCancellationRequested();
            return SecretResolutionResult.Unavailable;
        }
    }

    public async Task<bool> ValidateAsync(SecretBinding binding, CancellationToken cancellationToken = default) =>
        (await GetSecretAsync(binding, cancellationToken).ConfigureAwait(false)).IsResolved;

    private static SecretResolutionResult Failure(SecretStatus status) => status switch
    {
        SecretStatus.Absent => SecretResolutionResult.Unconfigured,
        SecretStatus.Denied => SecretResolutionResult.Unauthorized,
        SecretStatus.Invalid => SecretResolutionResult.Invalid,
        _ => SecretResolutionResult.Unavailable
    };
}
