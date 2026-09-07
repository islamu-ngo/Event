using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationAnalytics;
using MediatR;

namespace Explore.Application.Features.RegistrationAnalytics;

public sealed class GetRegistrationAnswerAnalyticsQueryHandler(
    IRegistrationAnswerAnalyticsRepository repository,
    ITenantContext tenantContext)
    : IRequestHandler<GetRegistrationAnswerAnalyticsQuery, RegistrationAnswerAnalyticsDto?>
{
    private const int MinimumCellSize = 3;

    public async Task<RegistrationAnswerAnalyticsDto?> Handle(
        GetRegistrationAnswerAnalyticsQuery request,
        CancellationToken cancellationToken)
    {
        var projection = await repository.GetEventFormVersionAnalyticsAsync(
            tenantContext.TenantId,
            request.EventId,
            request.FormId,
            request.FormVersionId,
            MinimumCellSize,
            cancellationToken);

        // The tenant is published as a trusted fact for the HAL self link: the analytics resource is
        // event-scoped, and the Local evaluator cannot resolve an event context without it.
        return projection is null ? null : new RegistrationAnswerAnalyticsDto(
            tenantContext.TenantId,
            projection.EventId,
            projection.FormId,
            projection.FormVersionId,
            projection.MinimumCellSize,
            projection.Fields.Select(field => new RegistrationAnswerFieldAggregateDto(
                field.FieldId,
                field.Namespace,
                field.Key,
                field.Label,
                field.FieldTypeId,
                field.FieldTypeCode,
                field.IsOperationallyFilterable,
                field.ResponseCount,
                field.Cells.Select(cell => new RegistrationAnswerAggregateCellDto(cell.Value, cell.Count)).ToArray(),
                field.Numeric is null ? null : new RegistrationAnswerNumericAggregateDto(
                    field.Numeric.Count,
                    field.Numeric.Min,
                    field.Numeric.Max,
                    field.Numeric.Average))).ToArray());
    }
}
