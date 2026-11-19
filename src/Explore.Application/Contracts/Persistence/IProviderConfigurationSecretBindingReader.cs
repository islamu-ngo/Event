using Explore.Domain.Secrets;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Reads current committed instance-scoped provider-configuration secret bindings without Runtime table grants.</summary>
public interface IProviderConfigurationSecretBindingReader
{
    Task<SecretBinding?> GetInstanceBindingAsync(string settingKey, CancellationToken cancellationToken = default);
}
