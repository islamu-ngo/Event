using System.Diagnostics;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.DTOs.CustomPropertyProjection.Validators;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using MediatR;

namespace Explore.Application.Features.EventCustomPropertyProjections.Handlers.Commands;

public class DrainCustomPropertyProjectionDirtyScopesCommandHandler
    : IRequestHandler<DrainCustomPropertyProjectionDirtyScopesCommand, BaseCommandResponse<DrainDirtyScopesResponseDto>>
{
    private readonly IEventCustomPropertyProjectionUpdater _eventProjectionUpdater;
    private readonly IEventSessionCustomPropertyProjectionUpdater _sessionProjectionUpdater;
    private readonly ProjectionMetrics _metrics;

    public DrainCustomPropertyProjectionDirtyScopesCommandHandler(
        IEventCustomPropertyProjectionUpdater eventProjectionUpdater,
        IEventSessionCustomPropertyProjectionUpdater sessionProjectionUpdater,
        ProjectionMetrics metrics)
    {
        _eventProjectionUpdater = eventProjectionUpdater;
        _sessionProjectionUpdater = sessionProjectionUpdater;
        _metrics = metrics;
    }

    public async Task<BaseCommandResponse<DrainDirtyScopesResponseDto>> Handle(
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
        var projectionName = request.RequestDto.ProjectionName;
        var stopwatch = Stopwatch.StartNew();

        var drainedCount = projectionName switch
        {
            IEventCustomPropertyProjectionUpdater.ProjectionName =>
                await _eventProjectionUpdater.DrainDirtyScopesForTenantAsync(tenantId, cancellationToken),

            IEventSessionCustomPropertyProjectionUpdater.ProjectionName =>
                await _sessionProjectionUpdater.DrainDirtyScopesForTenantAsync(tenantId, cancellationToken),

            _ => -1
        };

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
