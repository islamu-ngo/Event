namespace Explore.Application.DTOs.EventTemplateSync;

public sealed record ModifiedOptionDto(
    string Namespace,
    string Key,
    Guid CurrentConcurrencyStamp,
    IReadOnlyList<FieldChangeDto> FieldChanges);
