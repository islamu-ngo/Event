using System.ComponentModel.DataAnnotations;
using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Models.CustomProperties;

public class CustomPropertyValueModel
{
    public Guid? Id { get; set; }
    public Guid DefinitionId { get; set; }
    public Guid ParentId { get; set; }
    public int Ordinal { get; set; }

    public string? TextValue { get; set; }
    public double? NumberValue { get; set; }
    public bool? BooleanValue { get; set; }
    public DateTimeOffset? DateTimeValue { get; set; }
    public Guid? OptionId { get; set; }

    public static CustomPropertyValueModel FromEventDto(EventCustomPropertyValueDto dto)
    {
        return new CustomPropertyValueModel
        {
            Id = dto.Id,
            DefinitionId = dto.EventCustomPropertyDefinitionId ?? Guid.Empty,
            ParentId = dto.EventId ?? Guid.Empty,
            Ordinal = dto.Ordinal ?? 0,
            TextValue = dto.TextValue,
            NumberValue = dto.NumberValue,
            BooleanValue = dto.BooleanValue,
            DateTimeValue = dto.DateTimeValue,
            OptionId = dto.OptionId
        };
    }

    public static CustomPropertyValueModel FromEventSessionDto(EventSessionCustomPropertyValueDto dto)
    {
        return new CustomPropertyValueModel
        {
            Id = dto.Id,
            DefinitionId = dto.EventSessionCustomPropertyDefinitionId ?? Guid.Empty,
            ParentId = dto.EventSessionId ?? Guid.Empty,
            Ordinal = dto.Ordinal ?? 0,
            TextValue = dto.TextValue,
            NumberValue = dto.NumberValue,
            BooleanValue = dto.BooleanValue,
            DateTimeValue = dto.DateTimeValue,
            OptionId = dto.OptionId
        };
    }
}
