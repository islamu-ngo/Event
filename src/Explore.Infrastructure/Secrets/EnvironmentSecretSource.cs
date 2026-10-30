using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.ServiceDefaults.Secrets;
using SecretKit.Core;

namespace Explore.Infrastructure.Secrets;

/// <summary>Reads only the selected owner's frozen Environment or User Secrets generation.</summary>
public sealed class EnvironmentSecretSource : ISecretSource, ISecretRevisionOwner
{
    private readonly SecretAuthorityAcquisition _authority;

    public EnvironmentSecretSource(SecretAuthorityAcquisition authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        _authority = authority;
    }

    public SecretSourceType SourceType => SecretSourceType.EnvironmentVariable;

    public bool IsRevisionCurrent(SecretBinding binding, string? revision)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return _authority.Provider is SecretAuthorityKind.Environment or SecretAuthorityKind.UserSecrets
            && binding.SourceType == SourceType
            && SecretBindingRevisionInventory.IsCurrent(_authority, RuntimeSecretBinding.Capture(binding), revision);
    }

    public async Task<SecretResolutionResult> GetSecretAsync(
        SecretBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();
        RuntimeSecretBinding metadata = RuntimeSecretBinding.Capture(binding);
        binding = metadata.ToBinding();
        if (_authority.Provider is not (SecretAuthorityKind.Environment or SecretAuthorityKind.UserSecrets)
            || binding.SourceType != SourceType
            || !SecretBindingAdmission.AllowsBinding(binding,
                SecretAcquisitionPolicy.Roots(_authority.Selection.Consumer), null))
            return SecretResolutionResult.Invalid;

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(_authority.ReadTimeout);
        try
        {
            SecretDefinition definition = SecretDefinitionRegistry.GetRequired(binding.SettingKey);
            var prepared = await _authority.PrepareGroupAsync(
                [new(definition.DefaultInfisicalPath, binding.EnvironmentVariableName!, "runtime")],
                bounded.Token).ConfigureAwait(false);
            if (!prepared.IsSuccess)
            {
                bounded.Token.ThrowIfCancellationRequested();
                return Failure(prepared.Status);
            }
            using var lease = prepared.Value;
            using var read = lease.Read("runtime");
            bounded.Token.ThrowIfCancellationRequested();
            // The borrowed property also fences owner retirement before publication.
            _ = _authority.NativeClient;
            string value = read.Material.Reveal();
            if (value.Length == 0)
                return SecretResolutionResult.Invalid;
            string revision = SecretBindingRevisionInventory.Observe(_authority, metadata)!;
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
