namespace Explore.Secrets.Services;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.SetupLive;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Microsoft.Extensions.Options;

public sealed class SetupSecretBindingWriter(
    IOptions<SecretProviderOptions> options,
    ISecretBindingRepository bindings,
    IInfisicalClientFactory infisicalClients) :
    ISetupSecretBindingWriter,
    ISetupSecretBindingReadinessReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async Task<SetupSecretBindingWriteOutcome> GetReadinessAsync(
        Guid bindingId,
        string bindingKey,
        CancellationToken cancellationToken)
    {
        (_, _, SetupSecretBindingWriteOutcome outcome) = await ResolveDispatchAsync(
            bindingId,
            bindingKey,
            cancellationToken);
        return outcome;
    }

    public async Task<SetupSecretBindingWriteOutcome> WriteAsync(
        SetupSecretBindingWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _ = StrictUtf8.GetCharCount(request.SecretValue.Span);
        }
        catch (DecoderFallbackException)
        {
            return SetupSecretBindingWriteOutcome.Invalid;
        }

        (SecretBinding? binding, IInfisicalClient? client,
            SetupSecretBindingWriteOutcome readiness) = await ResolveDispatchAsync(
                request.BindingId,
                request.BindingKey,
                cancellationToken);
        if (readiness != SetupSecretBindingWriteOutcome.Ready)
            return readiness;

        try
        {
            bool written = await client!.WriteSecretAsync(
                    binding!.InfisicalEnvironment!,
                    binding.InfisicalPath!,
                    binding.InfisicalKey!,
                    request.SecretValue,
                    cancellationToken)
                .ConfigureAwait(false);
            return written
                ? SetupSecretBindingWriteOutcome.Ready
                : SetupSecretBindingWriteOutcome.Unavailable;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException exception) when (
            exception.StatusCode is HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden)
        {
            return SetupSecretBindingWriteOutcome.Unauthorized;
        }
        catch (UnauthorizedAccessException)
        {
            return SetupSecretBindingWriteOutcome.Unauthorized;
        }
#pragma warning disable CA1031 // Provider boundary translates all diagnostics to a value-free outcome.
        catch (Exception)
#pragma warning restore CA1031
        {
            return SetupSecretBindingWriteOutcome.Unavailable;
        }
    }

    private async Task<(SecretBinding? Binding, IInfisicalClient? Client,
        SetupSecretBindingWriteOutcome Outcome)> ResolveDispatchAsync(
        Guid bindingId,
        string bindingKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecretBinding? binding = await bindings.GetById(bindingId);
        if (binding is null
            || binding.Id != bindingId
            || !string.Equals(
                binding.SettingKey,
                bindingKey,
                StringComparison.Ordinal)
            || binding.Scope != SecretScope.Instance
            || binding.ScopeId is not null
            || !string.IsNullOrEmpty(binding.Qualifier))
        {
            return (null, null, SetupSecretBindingWriteOutcome.Invalid);
        }

        SecretSourceType? selectedSource = options.Value.Provider switch
        {
            SecretProviderType.Infisical => SecretSourceType.Infisical,
            SecretProviderType.Environment or SecretProviderType.UserSecrets =>
                SecretSourceType.EnvironmentVariable,
            _ => null
        };
        if (selectedSource != binding.SourceType)
            return (binding, null, SetupSecretBindingWriteOutcome.Invalid);
        if (selectedSource != SecretSourceType.Infisical)
            return (binding, null, SetupSecretBindingWriteOutcome.Unavailable);
        if (string.IsNullOrWhiteSpace(binding.InfisicalEnvironment)
            || string.IsNullOrWhiteSpace(binding.InfisicalPath)
            || string.IsNullOrWhiteSpace(binding.InfisicalKey))
        {
            return (binding, null, SetupSecretBindingWriteOutcome.Invalid);
        }
#pragma warning disable CA1031 // Provider boundary translates all diagnostics to a value-free outcome.
        try
        {
            IInfisicalClient? client = await infisicalClients
                .GetClientAsync(cancellationToken)
                .ConfigureAwait(false);
            return client is null
                ? (binding, null, SetupSecretBindingWriteOutcome.Unavailable)
                : (binding, client, SetupSecretBindingWriteOutcome.Ready);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
#pragma warning restore CA1031
        {
            return (binding, null, SetupSecretBindingWriteOutcome.Unavailable);
        }
    }
}

public sealed class SetupSecretBindingCommitmentAuthority(
    ISecretResolver resolver) : ISetupSecretBindingCommitmentAuthority
{
    private const string CommitmentUnavailable =
        "setup-secret-binding-commitment-unavailable";

    public async Task<SetupSecretBindingCommitment> CommitAsync(
        SetupSecretBindingCommitmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        SecretResolutionResult resolved = await resolver.ResolveAsync(
                SetupSecretBindingContractMetadata.CommitmentAuthorityKey,
                tenantId: null,
                cancellationToken)
            .ConfigureAwait(false);
        if (!resolved.IsResolved || string.IsNullOrEmpty(resolved.Value))
            throw new InvalidOperationException(CommitmentUnavailable);

        byte[]? key = null;
        byte[]? prefix = null;
        try
        {
            try
            {
                key = Convert.FromBase64String(resolved.Value);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    CommitmentUnavailable,
                    exception);
            }
            if (key.Length < 32)
                throw new InvalidOperationException(CommitmentUnavailable);

            prefix = Encoding.UTF8.GetBytes(
                $"setup-secret-binding-commitment-v1\n{request.TenantId:D}\n{request.ActorId:D}\n{request.EnrollmentId:D}\n{request.EnrollmentGeneration}\n{request.OperationKey:D}\n{request.BindingKey}\n");
            using IncrementalHash hmac = IncrementalHash.CreateHMAC(
                HashAlgorithmName.SHA256,
                key);
            hmac.AppendData(prefix);
            hmac.AppendData(request.SecretValue.Span);
            return new SetupSecretBindingCommitment(
                keyVersion: 1,
                Convert.ToHexString(hmac.GetHashAndReset()).ToLowerInvariant());
        }
        finally
        {
            if (key is not null)
                CryptographicOperations.ZeroMemory(key);
            if (prefix is not null)
                CryptographicOperations.ZeroMemory(prefix);
        }
    }
}

public sealed class ImmediateSetupSecretBindingCommitBarrier :
    ISetupSecretBindingCommitBarrier
{
    public Task WaitBeforeProviderDispatchAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
