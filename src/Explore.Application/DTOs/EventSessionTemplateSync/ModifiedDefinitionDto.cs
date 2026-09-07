namespace Explore.Application.DTOs.EventSessionTemplateSync;

public sealed record ModifiedDefinitionDto(
    string Namespace,
    string Key,
    Guid CurrentConcurrencyStamp,
    IReadOnlyList<FieldChangeDto> FieldChanges);
