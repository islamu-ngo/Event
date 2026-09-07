namespace Explore.Domain;

public sealed record RegistrationAnswerAnalyticsProjection(
    Guid EventId,
    Guid FormId,
    Guid FormVersionId,
    int MinimumCellSize,
    IReadOnlyList<RegistrationAnswerFieldAggregateProjection> Fields);

public sealed record RegistrationAnswerFieldAggregateProjection(
    Guid FieldId,
    string Namespace,
    string Key,
    string Label,
    int FieldTypeId,
    string FieldTypeCode,
    bool IsOperationallyFilterable,
    long ResponseCount,
    IReadOnlyList<RegistrationAnswerAggregateCellProjection> Cells,
    RegistrationAnswerNumericAggregateProjection? Numeric = null);

public sealed record RegistrationAnswerAggregateCellProjection(
    string Value,
    long Count);

public sealed record RegistrationAnswerNumericAggregateProjection(
    long Count,
    decimal Min,
    decimal Max,
    decimal Average);
