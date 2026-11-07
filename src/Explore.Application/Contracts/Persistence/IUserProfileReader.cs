using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Reads detached profile scalars with managed media evaluated in the current request's storage scope.</summary>
public interface IUserProfileReader
{
    Task<User?> GetAsync(Guid userId, CancellationToken cancellationToken);
}
