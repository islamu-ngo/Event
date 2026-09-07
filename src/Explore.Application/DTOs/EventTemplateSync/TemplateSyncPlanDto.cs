namespace Explore.Application.DTOs.EventTemplateSync;

public sealed record class TemplateSyncPlanDto
{
    public int TargetTemplateVersion { get; init; }
    public int BaseProvenanceVersion { get; init; }
    public IReadOnlyList<string> AddedDefinitionKeys { get; init; } = [];
    public IReadOnlyList<string> ModifiedDefinitionKeys { get; init; } = [];
    public IReadOnlyList<string> RetiredDefinitionKeys { get; init; } = [];
    public IReadOnlyList<string> AddedOptionKeys { get; init; } = [];
    public IReadOnlyList<string> ModifiedOptionKeys { get; init; } = [];
    public IReadOnlyList<string> RetiredOptionKeys { get; init; } = [];

    public int GetTotalChangeCount()
        => AddedDefinitionKeys.Count
         + ModifiedDefinitionKeys.Count
         + RetiredDefinitionKeys.Count
         + AddedOptionKeys.Count
         + ModifiedOptionKeys.Count
         + RetiredOptionKeys.Count;
}
