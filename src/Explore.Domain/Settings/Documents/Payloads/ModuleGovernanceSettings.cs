namespace Explore.Domain.Settings.Documents.Payloads;

public sealed record ModuleGovernanceSettings
{
    public bool IslamicModuleEnabled { get; init; } = true;

    public bool TechModuleEnabled { get; init; } = true;
}
