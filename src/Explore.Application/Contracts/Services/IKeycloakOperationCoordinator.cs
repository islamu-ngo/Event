using Explore.Domain.Keycloak;

namespace Explore.Application.Contracts.Services;

public sealed class KeycloakOperationOverlapException()
    : InvalidOperationException(
        "An unresolved Keycloak operation overlaps this realm.");

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
