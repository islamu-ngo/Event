namespace Explore.Application.DTOs.EventTemplateSync;

public sealed record FieldChangeDto(
    string FieldName,
    string? OldValue,
    string? NewValue,
    string ValueType);
