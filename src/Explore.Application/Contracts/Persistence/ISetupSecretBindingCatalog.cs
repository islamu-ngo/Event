namespace Explore.Application.Contracts.Persistence;

/// <summary>Locates setup instance bindings without exposing provider coordinates or secret values.</summary>
public interface ISetupSecretBindingCatalog
{
    Task<Guid?> FindInstanceIdAsync(string bindingKey, CancellationToken cancellationToken);
}
