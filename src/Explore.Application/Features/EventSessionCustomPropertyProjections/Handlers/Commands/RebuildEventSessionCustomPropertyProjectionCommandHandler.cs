using Explore.Application.Contracts.Operations;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.DTOs.CustomPropertyProjection.Validators;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain.Settings.Definitions;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventSessionCustomPropertyProjections.Handlers.Commands;

public class RebuildEventSessionCustomPropertyProjectionCommandHandler
    : ICommandHandler<RebuildEventSessionCustomPropertyProjectionCommand, BaseCommandResponse<RebuildProjectionResponseDto>>
{
    private readonly IEventSessionCustomPropertyProjectionUpdater _projectionUpdater;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ProjectionMetrics _metrics;
    private readonly ISerializableAttemptOwner _owner;
    private readonly IHumanAdministrativeActionPreparation _preparation;
    private readonly ITenantContext _tenantContext;
    private readonly IUserContext _userContext;
    private readonly IAdminContext _adminContext;
    private readonly IInstanceAdministratorCommitAuthority _instanceAuthority;
    private readonly ITenantAdministratorCommitAuthority _tenantAuthority;
    private readonly RequestAuthorization<RebuildEventSessionCustomPropertyProjectionCommand> _authorization;
    private readonly ILogger<RebuildEventSessionCustomPropertyProjectionCommandHandler> _logger;

    public RebuildEventSessionCustomPropertyProjectionCommandHandler(
        IEventSessionCustomPropertyProjectionUpdater projectionUpdater,
        ICustomPropertyQuotaResolver quotaResolver,
        ProjectionMetrics metrics,
        ISerializableAttemptOwner owner, ITenantContext tenantContext, IUserContext userContext, IAdminContext adminContext,
        IInstanceAdministratorCommitAuthority instanceAuthority, ITenantAdministratorCommitAuthority tenantAuthority,
        RequestAuthorization<RebuildEventSessionCustomPropertyProjectionCommand> authorization,
        ILogger<RebuildEventSessionCustomPropertyProjectionCommandHandler> logger,
        IHumanAdministrativeActionPreparation preparation)
    {
        _projectionUpdater = projectionUpdater;
        _quotaResolver = quotaResolver;
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

    public async Task<BaseCommandResponse<RebuildProjectionResponseDto>> ExecuteAsync(
        RebuildEventSessionCustomPropertyProjectionCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new RebuildProjectionRequestDtoValidator();
        var validationResult = await validator.ValidateAsync(request.RequestDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<RebuildProjectionResponseDto>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Session projection rebuild request validation failed.");
        }

        Guid tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty || tenantId != request.RequestDto.TenantId || !_userContext.IsAuthenticated
            || await _adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
            throw new AuthorizationException(ResourceKinds.CustomPropertyProjection, AuthorizationActions.Update);

        if (request.RequestDto.BatchSize.HasValue)
        {
            var maxBatchSize = await _quotaResolver.GetIntAsync(
                CustomPropertyQuotaSettingDefinitions.ProjectionRebuildBatchSize.Key,
                request.RequestDto.TenantId,
                cancellationToken);

            if (request.RequestDto.BatchSize.Value > maxBatchSize)
            {
                var scope = "event_session_custom_property_projection_rebuild";
                _metrics.RecordQuotaExceeded(
                    request.RequestDto.TenantId.ToString(),
                    IEventSessionCustomPropertyProjectionUpdater.ProjectionName,
                    CustomPropertyQuotaSettingDefinitions.ProjectionRebuildBatchSize.Key,
                    scope);

                return BaseCommandResponse.Quota<RebuildProjectionResponseDto>(
                    "Session projection rebuild request validation failed.",
                    new QuotaExceededDetails(
                        CustomPropertyQuotaSettingDefinitions.ProjectionRebuildBatchSize.Key,
                        maxBatchSize,
                        null,
                        request.RequestDto.BatchSize.Value,
                        scope,
                        request.RequestDto.TenantId));
            }
        }

        var startedAt = DateTimeOffset.UtcNow;
        var prepared = await _preparation.PrepareAsync(request, userId, cancellationToken);
        var result = await _owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, userId, token);
            await _authorization.AuthorizeAsync(request, _logger, token);
            if (!await _instanceAuthority.IsInstanceAdministratorForCommitAsync(userId, token)
                && !await _tenantAuthority.IsTenantAdministratorForCommitAsync(userId, tenantId, token))
                throw new AuthorizationException(ResourceKinds.CustomPropertyProjection, AuthorizationActions.Update);
            return await _projectionUpdater.RebuildForTenantAsync(tenantId, request.RequestDto.BatchSize, token);
        }, cancellationToken);
        var completedAt = DateTimeOffset.UtcNow;
        var durationSeconds = (completedAt - startedAt).TotalSeconds;

        _metrics.RecordRebuild(
            request.RequestDto.TenantId.ToString(),
            IEventSessionCustomPropertyProjectionUpdater.ProjectionName,
            result.RowsProcessed,
            result.RowsFailed,
            durationSeconds,
            result.LockAcquired);

        var message = result.LockAcquired
            ? $"Session projection rebuild completed. {result.RowsProcessed} rows processed, {result.DrainedDirtyScopes} dirty scopes drained."
            : "Session projection rebuild skipped — another rebuild is already in progress.";

        return BaseCommandResponse.Success(
            new RebuildProjectionResponseDto
            {
                LockAcquired = result.LockAcquired,
                RowsProcessed = result.RowsProcessed,
                RowsFailed = result.RowsFailed,
                DrainedDirtyScopes = result.DrainedDirtyScopes,
                StartedAt = startedAt,
                CompletedAt = completedAt
            },
            message);
    }
}
