using System.Diagnostics;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.DTOs.CustomPropertyProjection.Validators;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventCustomPropertyProjections.Handlers.Commands;

public class DrainCustomPropertyProjectionDirtyScopesCommandHandler
    : ICommandHandler<DrainCustomPropertyProjectionDirtyScopesCommand, BaseCommandResponse<DrainDirtyScopesResponseDto>>
{
    private readonly IEventCustomPropertyProjectionUpdater _eventProjectionUpdater;
    private readonly IEventSessionCustomPropertyProjectionUpdater _sessionProjectionUpdater;
    private readonly ProjectionMetrics _metrics;
    private readonly ISerializableAttemptOwner _owner;
    private readonly IHumanAdministrativeActionPreparation _preparation;
    private readonly ITenantContext _tenantContext;
    private readonly IUserContext _userContext;
    private readonly IAdminContext _adminContext;
    private readonly IInstanceAdministratorCommitAuthority _instanceAuthority;
    private readonly ITenantAdministratorCommitAuthority _tenantAuthority;
    private readonly RequestAuthorization<DrainCustomPropertyProjectionDirtyScopesCommand> _authorization;
    private readonly ILogger<DrainCustomPropertyProjectionDirtyScopesCommandHandler> _logger;

    public DrainCustomPropertyProjectionDirtyScopesCommandHandler(
        IEventCustomPropertyProjectionUpdater eventProjectionUpdater,
        IEventSessionCustomPropertyProjectionUpdater sessionProjectionUpdater,
        ProjectionMetrics metrics,
        ISerializableAttemptOwner owner, ITenantContext tenantContext, IUserContext userContext, IAdminContext adminContext,
        IInstanceAdministratorCommitAuthority instanceAuthority, ITenantAdministratorCommitAuthority tenantAuthority,
        RequestAuthorization<DrainCustomPropertyProjectionDirtyScopesCommand> authorization,
        ILogger<DrainCustomPropertyProjectionDirtyScopesCommandHandler> logger,
        IHumanAdministrativeActionPreparation preparation)
    {
        _eventProjectionUpdater = eventProjectionUpdater;
        _sessionProjectionUpdater = sessionProjectionUpdater;
        _metrics = metrics;
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

    public async Task<BaseCommandResponse<DrainDirtyScopesResponseDto>> ExecuteAsync(
        DrainCustomPropertyProjectionDirtyScopesCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new DrainDirtyScopesRequestDtoValidator();
        var validationResult = await validator.ValidateAsync(request.RequestDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<DrainDirtyScopesResponseDto>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Drain request validation failed.");
        }

        var tenantId = request.RequestDto.TenantId;
        if (tenantId == Guid.Empty || tenantId != _tenantContext.TenantId || !_userContext.IsAuthenticated
            || await _adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
            throw new AuthorizationException(ResourceKinds.CustomPropertyProjection, AuthorizationActions.Update);
        var projectionName = request.RequestDto.ProjectionName;
        var stopwatch = Stopwatch.StartNew();

        var prepared = await _preparation.PrepareAsync(request, userId, cancellationToken);
        var drainedCount = await _owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, userId, token);
            await _authorization.AuthorizeAsync(request, _logger, token);
            if (!await _instanceAuthority.IsInstanceAdministratorForCommitAsync(userId, token)
                && !await _tenantAuthority.IsTenantAdministratorForCommitAsync(userId, tenantId, token))
                throw new AuthorizationException(ResourceKinds.CustomPropertyProjection, AuthorizationActions.Update);
            return projectionName switch
            {
                IEventCustomPropertyProjectionUpdater.ProjectionName =>
                    await _eventProjectionUpdater.DrainDirtyScopesForTenantAsync(tenantId, token),
                IEventSessionCustomPropertyProjectionUpdater.ProjectionName =>
                    await _sessionProjectionUpdater.DrainDirtyScopesForTenantAsync(tenantId, token),
                _ => -1
            };
        }, cancellationToken);

        stopwatch.Stop();

        if (drainedCount < 0)
        {
            _metrics.RecordDrainFailure(tenantId.ToString(), projectionName);
            return BaseCommandResponse.Validation<DrainDirtyScopesResponseDto>(
                [$"ProjectionName must be '{IEventCustomPropertyProjectionUpdater.ProjectionName}' or '{IEventSessionCustomPropertyProjectionUpdater.ProjectionName}'."],
                $"Unknown projection name: '{projectionName}'.");
        }

        _metrics.RecordDrain(tenantId.ToString(), projectionName, drainedCount, stopwatch.Elapsed.TotalSeconds);

        return BaseCommandResponse.Success(
            new DrainDirtyScopesResponseDto
            {
                DrainedCount = drainedCount,
                DrainedAt = DateTimeOffset.UtcNow
            },
            $"Drained {drainedCount} dirty scope(s).");
    }
}
