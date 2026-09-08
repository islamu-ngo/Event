namespace Explore.Application.DTOs.EventSessionTemplateSync;

public sealed record ModifiedOptionDto(
    string Namespace,
    string Key,
    Guid CurrentConcurrencyStamp,
    IReadOnlyList<FieldChangeDto> FieldChanges);
