namespace Explore.Application.DTOs.EventCustomProperty;

public sealed record EventCustomPropertyValueDto
{
    public Guid Id { get; init; }
    public Guid EventCustomPropertyDefinitionId { get; init; }
    public Guid EventId { get; init; }
    public int Ordinal { get; init; }
    public string? TextValue { get; init; }
    public decimal? NumberValue { get; init; }
    public bool? BooleanValue { get; init; }
    public DateTimeOffset? DateTimeValue { get; init; }
    public Guid? OptionId { get; init; }
}
