using Explore.Application.Contracts.Persistence;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventCustomPropertyProjections.Handlers.Commands;

public class RebuildSingleEventCustomPropertyProjectionCommandHandler
    : ICommandHandler<RebuildSingleEventCustomPropertyProjectionCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventCustomPropertyProjectionUpdater _projectionUpdater;
    private readonly ISerializableAttemptOwner _owner;
    private readonly IHumanAdministrativeActionPreparation _preparation;
    private readonly ITenantContext _tenantContext;
    private readonly IUserContext _userContext;
    private readonly IAdminContext _adminContext;
    private readonly IInstanceAdministratorCommitAuthority _instanceAuthority;
    private readonly ITenantAdministratorCommitAuthority _tenantAuthority;
    private readonly RequestAuthorization<RebuildSingleEventCustomPropertyProjectionCommand> _authorization;
    private readonly ILogger<RebuildSingleEventCustomPropertyProjectionCommandHandler> _logger;

    public RebuildSingleEventCustomPropertyProjectionCommandHandler(
        IEventCustomPropertyProjectionUpdater projectionUpdater,
        ISerializableAttemptOwner owner,
        ITenantContext tenantContext, IUserContext userContext, IAdminContext adminContext,
        IInstanceAdministratorCommitAuthority instanceAuthority, ITenantAdministratorCommitAuthority tenantAuthority,
        RequestAuthorization<RebuildSingleEventCustomPropertyProjectionCommand> authorization,
        ILogger<RebuildSingleEventCustomPropertyProjectionCommandHandler> logger,
        IHumanAdministrativeActionPreparation preparation)
    {
        _projectionUpdater = projectionUpdater;
        _owner = owner;
        _preparation = preparation;
        _tenantContext = tenantContext;
        _userContext = userContext;
        _adminContext = adminContext;
        _instanceAuthority = instanceAuthority;
        _tenantAuthority = tenantAuthority;
        _authorization = authorization;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        RebuildSingleEventCustomPropertyProjectionCommand request,
        CancellationToken cancellationToken)
    {
        if (request.EventId == Guid.Empty)
        {
            return BaseCommandResponse.Validation<Guid>(["EventId is required."], "EventId is required.");
        }

        Guid tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty || !_userContext.IsAuthenticated
            || await _adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
            throw new AuthorizationException(ResourceKinds.CustomPropertyProjection, AuthorizationActions.Update);
        var prepared = await _preparation.PrepareAsync(request, userId, cancellationToken);
        await _owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, userId, token);
            await _authorization.AuthorizeAsync(request, _logger, token);
            if (!await _instanceAuthority.IsInstanceAdministratorForCommitAsync(userId, token)
                && !await _tenantAuthority.IsTenantAdministratorForCommitAsync(userId, tenantId, token))
                throw new AuthorizationException(ResourceKinds.CustomPropertyProjection, AuthorizationActions.Update);
            await _projectionUpdater.RefreshForEventAsync(request.EventId, token);
            return true;
        }, cancellationToken);

        return BaseCommandResponse.Success(request.EventId, "Event projection rows refreshed successfully.");
    }
}
