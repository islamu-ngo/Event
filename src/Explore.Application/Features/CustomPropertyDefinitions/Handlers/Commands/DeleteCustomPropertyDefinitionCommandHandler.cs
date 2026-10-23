using Explore.Application.Authorization;
using Explore.Application.Caching;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;
using Explore.Application.Contracts.Operations;
using Explore.Domain;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.CustomPropertyDefinitions.Handlers.Commands;

public class DeleteCustomPropertyDefinitionCommandHandler(
    ICustomPropertyDefinitionRepository repository,
    HybridCache cache,
    ISerializableAttemptOwner owner,
    ITenantContext tenantContext,
    IUserContext userContext,
    IAdminContext adminContext,
    ITenantAdministratorCommitAuthority tenantAuthority,
    IInstanceAdministratorCommitAuthority instanceAuthority,
    IHumanAdministrativeActionPreparation preparation,
    RequestAuthorization<DeleteCustomPropertyDefinitionCommand> authorization,
    ILogger<DeleteCustomPropertyDefinitionCommandHandler> logger)
    : ICommandHandler<DeleteCustomPropertyDefinitionCommand, bool>
{
    public async Task<bool> ExecuteAsync(DeleteCustomPropertyDefinitionCommand request, CancellationToken cancellationToken)
    {
        Guid tenantId = tenantContext.TenantId;
        if (!userContext.IsAuthenticated || tenantId == Guid.Empty
            || await adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
            return false;
        await authorization.AuthorizeAsync(request, logger, cancellationToken);
        var prepared = await preparation.PrepareAsync(request, userId, cancellationToken);
        CustomPropertyDefinition? deletedDefinition = null;
        bool deleted = await owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, userId, token);
            deletedDefinition = null;
            if (!await instanceAuthority.IsInstanceAdministratorForCommitAsync(userId, token)
                && !await tenantAuthority.IsTenantAdministratorForCommitAsync(userId, tenantId, token))
                return false;
            var definition = await repository.GetTrackedDefinitionWithOptions(request.Id, token);
            if (definition is null || definition.TenantId != tenantId || definition.IsDeleted)
                return false;
            if (!await repository.DeleteDefinition(definition.Id, token))
                return false;
            deletedDefinition = definition;
            return true;
        }, cancellationToken);
        if (!deleted || deletedDefinition is null)
            return false;

        await cache.RemoveByTagAsync(
            CacheTags.CustomPropertyDefinitionListsByScope(deletedDefinition.TenantId, deletedDefinition.EntityTypeName),
            CancellationToken.None);

        return true;
    }
}
