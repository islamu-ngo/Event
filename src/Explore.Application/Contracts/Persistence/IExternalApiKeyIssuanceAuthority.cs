using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Evaluates exact persisted owner authority under transaction-bound revocation fences.</summary>
public interface IExternalApiKeyIssuanceAuthority
{
    Task<bool> IsAuthorizedForCommitAsync(
        Guid principalId,
        Guid? tenantId,
        ExternalApiKeyOwnerType ownerType,
        Guid ownerId,
        CancellationToken cancellationToken);
}
