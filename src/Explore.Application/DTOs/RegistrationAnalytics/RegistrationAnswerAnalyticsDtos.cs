namespace Explore.Application.DTOs.RegistrationAnalytics;

public sealed record RegistrationAnswerAnalyticsDto(
    Guid TenantId,
    Guid EventId,
    Guid FormId,
    Guid FormVersionId,
    int MinimumCellSize,
    IReadOnlyList<RegistrationAnswerFieldAggregateDto> Fields);

public sealed record RegistrationAnswerFieldAggregateDto(
    Guid FieldId,
    string Namespace,
    string Key,
    string Label,
    int FieldTypeId,
    string FieldTypeCode,
    bool IsOperationallyFilterable,
    long ResponseCount,
    IReadOnlyList<RegistrationAnswerAggregateCellDto> Cells,
    RegistrationAnswerNumericAggregateDto? Numeric = null);

public sealed record RegistrationAnswerAggregateCellDto(
    string Value,
    long Count);

public sealed record RegistrationAnswerNumericAggregateDto(
    long Count,
    decimal Min,
    decimal Max,
    decimal Average);
