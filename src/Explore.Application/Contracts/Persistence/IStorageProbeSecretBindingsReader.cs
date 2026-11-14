using Explore.Domain.Secrets;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Reads detached storage secret bindings and canonical mapped metadata within the
/// probe's original active source transaction. Does not resolve external secrets.
/// </summary>
public interface IStorageProbeSecretBindingsReader
{
    Task<(IReadOnlyList<SecretBinding> Bindings, string Metadata)> ReadAsync(
        Guid ambientTenant, CancellationToken cancellationToken);
}
