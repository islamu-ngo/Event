using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.DTOs.CustomPropertyProjection.Validators;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain.Settings.Definitions;
using MediatR;

namespace Explore.Application.Features.EventSessionCustomPropertyProjections.Handlers.Commands;

public class RebuildEventSessionCustomPropertyProjectionCommandHandler
    : IRequestHandler<RebuildEventSessionCustomPropertyProjectionCommand, BaseCommandResponse<RebuildProjectionResponseDto>>
{
    private readonly IEventSessionCustomPropertyProjectionUpdater _projectionUpdater;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ProjectionMetrics _metrics;

    public RebuildEventSessionCustomPropertyProjectionCommandHandler(
        IEventSessionCustomPropertyProjectionUpdater projectionUpdater,
        ICustomPropertyQuotaResolver quotaResolver,
        ProjectionMetrics metrics)
    {
        _projectionUpdater = projectionUpdater;
        _quotaResolver = quotaResolver;
        _metrics = metrics;
    }

    public async Task<BaseCommandResponse<RebuildProjectionResponseDto>> Handle(
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
        var result = await _projectionUpdater.RebuildForTenantAsync(
            request.RequestDto.TenantId,
            request.RequestDto.BatchSize,
            cancellationToken);
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
