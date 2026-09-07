namespace Explore.Application.DTOs.EventSessionTemplateSync;

public sealed record TemplateDiffDto(
    int TargetTemplateVersion,
    int BaseProvenanceVersion,
    IReadOnlyList<AddedDefinitionDto> AddedDefinitions,
    IReadOnlyList<ModifiedDefinitionDto> ModifiedDefinitions,
    IReadOnlyList<RetiredDefinitionDto> RetiredDefinitions,
    IReadOnlyList<AddedOptionDto> AddedOptions,
    IReadOnlyList<ModifiedOptionDto> ModifiedOptions,
    IReadOnlyList<RetiredOptionDto> RetiredOptions,
    IReadOnlyList<UntouchedLocalDefinitionDto> UntouchedLocalDefinitions);
