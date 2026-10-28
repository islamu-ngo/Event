namespace Explore.Application.Contracts.Persistence;

/// <summary>Reads the identity of an unqualified instance setup binding without provider coordinates.</summary>
public interface ISetupSecretBindingIdentityReader
{
    Task<Guid?> ReadAsync(string bindingKey, CancellationToken cancellationToken);
}
