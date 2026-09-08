namespace Explore.Application.DTOs.Localization;

public sealed record UpdateLocalizationGovernanceDto
{
    public LocalizationTmsUpdateDto? Tms { get; init; }
    public LocalizationLanguagePolicyUpdateDto? Languages { get; init; }
    public LocalizationRuntimeUpdateDto? Runtime { get; init; }
}

public sealed record LocalizationTmsUpdateDto
{
    public required string Provider { get; init; }
    public string? ApiUrl { get; init; }
    public string? ProjectId { get; init; }
    public string? Component { get; init; }
}

public sealed record LocalizationLanguagePolicyUpdateDto
{
    private IReadOnlyList<string> _enabledLanguages = Array.AsReadOnly(Array.Empty<string>());

    public required string DefaultLanguage { get; init; }
    public IReadOnlyList<string> EnabledLanguages
    {
        get => _enabledLanguages;
        init => _enabledLanguages = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
    public required string FallbackLanguage { get; init; }
}

public sealed record LocalizationRuntimeUpdateDto
{
    public bool ClientPickerEnabled { get; init; }
    public bool ForceOfflineMode { get; init; }
}
