namespace Explore.Application.DTOs.EventTemplateSync;

public sealed record ModifiedDefinitionDto(
    string Namespace,
    string Key,
    Guid CurrentConcurrencyStamp,
    IReadOnlyList<FieldChangeDto> FieldChanges);
