namespace Explore.Application.DTOs.EventSessionCustomProperty;

public sealed record EventSessionCustomPropertyValueDto
{
    public Guid Id { get; init; }
    public Guid EventSessionCustomPropertyDefinitionId { get; init; }
    public Guid EventSessionId { get; init; }
    public int Ordinal { get; init; }
    public string? TextValue { get; init; }
    public decimal? NumberValue { get; init; }
    public bool? BooleanValue { get; init; }
    public DateTimeOffset? DateTimeValue { get; init; }
    public Guid? OptionId { get; init; }
}
