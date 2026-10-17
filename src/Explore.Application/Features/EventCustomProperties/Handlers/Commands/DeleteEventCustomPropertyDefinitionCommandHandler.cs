using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventCustomProperties.Requests.Commands;
using Explore.Application.Contracts.Operations;
using Explore.Domain;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventCustomProperties.Handlers.Commands;

public class DeleteEventCustomPropertyDefinitionCommandHandler(
    IEventCustomPropertyRepository repository,
    IEventCustomPropertyProjectionUpdater projectionUpdater,
    ISerializableAttemptOwner owner,
    HybridCache cache,
    ITenantContext tenantContext,
    IUserContext userContext,
    IAdminContext adminContext,
    ITenantAdministratorCommitAuthority tenantAuthority,
    IInstanceAdministratorCommitAuthority instanceAuthority,
    RequestAuthorization<DeleteEventCustomPropertyDefinitionCommand> authorization,
    ILogger<DeleteEventCustomPropertyDefinitionCommandHandler> logger,
    IHumanAdministrativeActionPreparation preparation)
    : ICommandHandler<DeleteEventCustomPropertyDefinitionCommand, bool>
{
    public async Task<bool> ExecuteAsync(DeleteEventCustomPropertyDefinitionCommand request, CancellationToken cancellationToken)
    {
        Guid tenantId = tenantContext.TenantId;
        if (!userContext.IsAuthenticated || tenantId == Guid.Empty
            || await adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
            return false;
        await authorization.AuthorizeAsync(request, logger, cancellationToken);
        EventCustomPropertyDefinition? deletedDefinition = null;
        var prepared = await preparation.PrepareAsync(request, userId, cancellationToken);
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
            await projectionUpdater.RemoveForDefinitionAsync(definition.Id, token);
            if (!await repository.DeleteDefinition(definition.Id, token))
                return false;
            deletedDefinition = definition;
            return true;
        }, cancellationToken);
        if (!deleted || deletedDefinition is null)
            return false;

        await cache.RemoveByTagAsync(
            EventCustomPropertyCache.ListsByEvent(deletedDefinition.TenantId, deletedDefinition.EventId),
            CancellationToken.None);

        return true;
    }
}
