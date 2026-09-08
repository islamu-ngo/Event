namespace Explore.Application.DTOs.CustomPropertyProjection;

public sealed record CustomPropertyFilterCriterion
{
    private IReadOnlyList<Guid>? _optionIds;

    public required string Namespace { get; init; }

    public required string Key { get; init; }

    public CustomPropertyFilterOperator Operator { get; init; } = CustomPropertyFilterOperator.Equals;

    public string? Value { get; init; }

    public Guid? OptionId { get; init; }

    public IReadOnlyList<Guid>? OptionIds
    {
        get => _optionIds;
        init => _optionIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    public decimal? MinNumber { get; init; }

    public decimal? MaxNumber { get; init; }

    public DateTimeOffset? DateFrom { get; init; }

    public DateTimeOffset? DateTo { get; init; }
}

public enum CustomPropertyFilterOperator
{
    Equals,
    Contains,
    Exists,
    BooleanTrue,
    OptionEquals,
    OptionIn,
    NumberRange,
    DateRange
}
