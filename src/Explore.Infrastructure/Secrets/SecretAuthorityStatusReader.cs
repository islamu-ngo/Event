using Explore.Application.Contracts.Secrets;
using Explore.ServiceDefaults.Secrets;

namespace Explore.Infrastructure.Secrets;

/// <summary>Reports selected-owner authentication health, not remote secret or mutation permission.</summary>
public sealed class SecretAuthorityStatusReader(SecretAuthorityAcquisition owner) : ISecretAuthorityStatusReader
{
    public async Task<SecretAuthorityStatusSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var operation = owner.BeginOperation(cancellationToken);
            if (owner.Provider == SecretAuthorityKind.Infisical)
            {
                if (owner.AuthenticationSession is not { } session)
                    return Degraded();
                var result = await session.GetTokenAsync(operation.Token).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    operation.Token.ThrowIfCancellationRequested();
                    return Degraded();
                }
                using var token = result.Value;
                operation.Token.ThrowIfCancellationRequested();
            }
            return new(owner.Provider.ToString(), "configured", "none");
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Degraded();
        }
    }

    private SecretAuthorityStatusSnapshot Degraded() =>
        new(owner.Provider.ToString(), "degraded", "restore_selected_secret_authority");
}
