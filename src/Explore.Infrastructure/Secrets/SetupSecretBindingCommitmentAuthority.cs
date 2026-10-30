using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.SetupLive;

namespace Explore.Infrastructure.Secrets;

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
