using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.ExternalApiKeys;

internal static class ExternalApiKeyManagementAuthority
{
    internal static Task<bool> IsAuthorizedForCommitAsync(
        ExternalApiKey key, Guid principalId, IExternalApiKeyIssuanceAuthority ownerAuthority,
        IInstanceAdministratorCommitAuthority instanceAuthority, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (key.OwnerType == ExternalApiKeyOwnerType.InstanceAdmin)
            return key.TenantId is null
                ? instanceAuthority.IsInstanceAdministratorForCommitAsync(principalId, cancellationToken)
                : Task.FromResult(false);
        return ownerAuthority.IsAuthorizedForCommitAsync(
            principalId, key.TenantId, key.OwnerType, key.OwnerId, cancellationToken);
    }

    internal static bool HasSameOwner(ExternalApiKey before, ExternalApiKey current) =>
        before.Id == current.Id && before.TenantId == current.TenantId
        && before.OwnerType == current.OwnerType && before.OwnerId == current.OwnerId;

    internal static void RegisterFinalAdmission(
        ExternalApiKey original, Guid principalId, IExternalApiKeyRepository keys,
        IExternalApiKeyIssuanceAuthority ownerAuthority,
        IInstanceAdministratorCommitAuthority instanceAuthority, IHumanActionCommitFinalizer finalizer)
    {
        finalizer.Register(async token =>
        {
            var current = await keys.GetForTenantAsync(original.Id, token);
            if (current is null || !HasSameOwner(original, current)
                || !await IsAuthorizedForCommitAsync(current, principalId, ownerAuthority, instanceAuthority, token))
                throw new UnauthorizedAccessException("Original key ownership and current management authority are required.");
        }, _ => { });
    }
}
