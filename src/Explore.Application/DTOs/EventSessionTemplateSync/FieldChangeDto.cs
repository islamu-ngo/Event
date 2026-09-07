namespace Explore.Application.DTOs.EventSessionTemplateSync;

public sealed record FieldChangeDto(
    string FieldName,
    string? OldValue,
    string? NewValue,
    string ValueType);
