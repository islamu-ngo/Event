using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationAnswerAnalyticsRepository(ExploreDbContext dbContext) : IRegistrationAnswerAnalyticsRepository
{
    public async Task<RegistrationAnswerAnalyticsProjection?> GetEventFormVersionAnalyticsAsync(
        Guid tenantId,
        Guid eventId,
        Guid formId,
        Guid formVersionId,
        int minimumCellSize,
        CancellationToken cancellationToken)
    {
        var fields = await dbContext.RegistrationFormFields
            .AsNoTracking()
            .Where(field => field.TenantId == tenantId &&
                            field.EventId == eventId &&
                            field.RegistrationFormId == formId &&
                            field.RegistrationFormVersionId == formVersionId &&
                            field.IsAnalyticsRelevant &&
                            !field.RequiresExplicitConsent &&
                            !field.IsDeleted)
            .OrderBy(field => field.Ordinal)
            .Select(field => new FieldSeed(
                field.Id,
                field.Namespace,
                field.Key,
                field.Label,
                field.FieldTypeId,
                field.IsOperationallyFilterable))
            .ToArrayAsync(cancellationToken);

        fields = fields
            .Where(field => FormVersionRules.IsAggregatableFieldType((RegistrationFieldTypeEnum)field.FieldTypeId))
            .ToArray();
        if (fields.Length == 0)
        {
            return new RegistrationAnswerAnalyticsProjection(eventId, formId, formVersionId, minimumCellSize, []);
        }

        var aggregates = new List<RegistrationAnswerFieldAggregateProjection>(fields.Length);
        foreach (FieldSeed field in fields)
        {
            RegistrationAnswerFieldAggregateProjection? aggregate = (RegistrationFieldTypeEnum)field.FieldTypeId switch
            {
                RegistrationFieldTypeEnum.Boolean => await BooleanAggregateAsync(field, tenantId, minimumCellSize, cancellationToken),
                RegistrationFieldTypeEnum.Integer or RegistrationFieldTypeEnum.Rating => await IntegerAggregateAsync(field, tenantId, minimumCellSize, cancellationToken),
                RegistrationFieldTypeEnum.Decimal => await DecimalAggregateAsync(field, tenantId, minimumCellSize, cancellationToken),
                RegistrationFieldTypeEnum.Date => await DateAggregateAsync(field, tenantId, minimumCellSize, cancellationToken),
                RegistrationFieldTypeEnum.SingleChoice or RegistrationFieldTypeEnum.MultipleChoice => await OptionAggregateAsync(field, tenantId, minimumCellSize, cancellationToken),
                _ => null
            };

            if (aggregate is not null)
            {
                aggregates.Add(aggregate);
            }
        }

        return new RegistrationAnswerAnalyticsProjection(eventId, formId, formVersionId, minimumCellSize, aggregates);
    }

    private async Task<RegistrationAnswerFieldAggregateProjection?> BooleanAggregateAsync(
        FieldSeed field,
        Guid tenantId,
        int minimumCellSize,
        CancellationToken cancellationToken)
    {
        var cells = await dbContext.RegistrationAnswers
            .AsNoTracking()
            .Where(answer => answer.TenantId == tenantId &&
                             answer.RegistrationFormFieldId == field.Id &&
                             answer.SensitiveAnswerValueId == null &&
                             answer.BooleanValue != null &&
                             !answer.IsDeleted)
            .GroupBy(answer => answer.BooleanValue!.Value)
            .Select(group => new RegistrationAnswerAggregateCellProjection(group.Key ? "true" : "false", group.LongCount()))
            .ToArrayAsync(cancellationToken);

        return Aggregate(field, cells, minimumCellSize);
    }

    private async Task<RegistrationAnswerFieldAggregateProjection?> IntegerAggregateAsync(
        FieldSeed field,
        Guid tenantId,
        int minimumCellSize,
        CancellationToken cancellationToken)
    {
        var values = await dbContext.RegistrationAnswers
            .AsNoTracking()
            .Where(answer => answer.TenantId == tenantId &&
                             answer.RegistrationFormFieldId == field.Id &&
                             answer.SensitiveAnswerValueId == null &&
                             answer.IntegerValue != null &&
                             !answer.IsDeleted)
            .Select(answer => (decimal)answer.IntegerValue!.Value)
            .ToArrayAsync(cancellationToken);

        return NumericAggregate(field, values, minimumCellSize);
    }

    private async Task<RegistrationAnswerFieldAggregateProjection?> DecimalAggregateAsync(
        FieldSeed field,
        Guid tenantId,
        int minimumCellSize,
        CancellationToken cancellationToken)
    {
        decimal[] values = await dbContext.RegistrationAnswers
            .AsNoTracking()
            .Where(answer => answer.TenantId == tenantId &&
                             answer.RegistrationFormFieldId == field.Id &&
                             answer.SensitiveAnswerValueId == null &&
                             answer.DecimalValue != null &&
                             !answer.IsDeleted)
            .Select(answer => answer.DecimalValue!.Value)
            .ToArrayAsync(cancellationToken);

        return NumericAggregate(field, values, minimumCellSize);
    }

    private async Task<RegistrationAnswerFieldAggregateProjection?> DateAggregateAsync(
        FieldSeed field,
        Guid tenantId,
        int minimumCellSize,
        CancellationToken cancellationToken)
    {
        var cells = await dbContext.RegistrationAnswers
            .AsNoTracking()
            .Where(answer => answer.TenantId == tenantId &&
                             answer.RegistrationFormFieldId == field.Id &&
                             answer.SensitiveAnswerValueId == null &&
                             answer.DateValue != null &&
                             !answer.IsDeleted)
            .GroupBy(answer => new { answer.DateValue!.Value.Year, answer.DateValue!.Value.Month })
            .Select(group => new RegistrationAnswerAggregateCellProjection($"{group.Key.Year:D4}-{group.Key.Month:D2}", group.LongCount()))
            .ToArrayAsync(cancellationToken);

        return Aggregate(field, cells, minimumCellSize);
    }

    private async Task<RegistrationAnswerFieldAggregateProjection?> OptionAggregateAsync(
        FieldSeed field,
        Guid tenantId,
        int minimumCellSize,
        CancellationToken cancellationToken)
    {
        var cells = await dbContext.RegistrationAnswers
            .AsNoTracking()
            .Where(answer => answer.TenantId == tenantId &&
                             answer.RegistrationFormFieldId == field.Id &&
                             answer.SensitiveAnswerValueId == null &&
                             answer.SelectedOptionId != null &&
                             !answer.IsDeleted)
            .Join(dbContext.RegistrationFormFieldOptions.AsNoTracking(),
                answer => new { answer.TenantId, OptionId = answer.SelectedOptionId!.Value },
                option => new { option.TenantId, OptionId = option.Id },
                (answer, option) => new { option.Key })
            .GroupBy(value => value.Key)
            .Select(group => new RegistrationAnswerAggregateCellProjection(group.Key, group.LongCount()))
            .ToArrayAsync(cancellationToken);

        return Aggregate(field, cells, minimumCellSize);
    }

    private static RegistrationAnswerFieldAggregateProjection? NumericAggregate(
        FieldSeed field,
        IReadOnlyCollection<decimal> values,
        int minimumCellSize) => values.Count < minimumCellSize || !HasSafeNumericBounds(values, minimumCellSize)
        ? null
        : new RegistrationAnswerFieldAggregateProjection(
            field.Id,
            field.Namespace,
            field.Key,
            field.Label,
            field.FieldTypeId,
            Code((RegistrationFieldTypeEnum)field.FieldTypeId),
            field.IsOperationallyFilterable,
            values.Count,
            [],
            new RegistrationAnswerNumericAggregateProjection(values.Count, values.Min(), values.Max(), values.Average()));

    private static bool HasSafeNumericBounds(IReadOnlyCollection<decimal> values, int minimumCellSize)
    {
        decimal min = values.Min();
        decimal max = values.Max();
        return values.LongCount(value => value == min) >= minimumCellSize
            && values.LongCount(value => value == max) >= minimumCellSize;
    }

    private static RegistrationAnswerFieldAggregateProjection? Aggregate(
        FieldSeed field,
        IReadOnlyCollection<RegistrationAnswerAggregateCellProjection> cells,
        int minimumCellSize)
    {
        RegistrationAnswerAggregateCellProjection[] safeCells = [.. cells.Where(cell => cell.Count >= minimumCellSize).OrderBy(cell => cell.Value)];
        long responseCount = safeCells.Sum(cell => cell.Count);
        return responseCount < minimumCellSize
            ? null
            : new RegistrationAnswerFieldAggregateProjection(
                field.Id,
                field.Namespace,
                field.Key,
                field.Label,
                field.FieldTypeId,
                Code((RegistrationFieldTypeEnum)field.FieldTypeId),
                field.IsOperationallyFilterable,
                responseCount,
                safeCells);
    }

    private static string Code(RegistrationFieldTypeEnum fieldType) => fieldType switch
    {
        RegistrationFieldTypeEnum.SingleChoice => "SINGLE_CHOICE",
        RegistrationFieldTypeEnum.MultipleChoice => "MULTIPLE_CHOICE",
        _ => fieldType.ToString().ToUpperInvariant()
    };

    private sealed record FieldSeed(
        Guid Id,
        string Namespace,
        string Key,
        string Label,
        int FieldTypeId,
        bool IsOperationallyFilterable);
}
