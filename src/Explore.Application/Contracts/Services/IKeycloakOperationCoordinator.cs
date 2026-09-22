using Explore.Domain.Keycloak;

namespace Explore.Application.Contracts.Services;

public interface IKeycloakOperationCoordinator
{
    Task<T> ExecuteAsync<T>(
        KeycloakOperation operation,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default);

    Task RequestCancellationAsync(
        Guid operationId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default);
}
