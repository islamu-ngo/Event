namespace Explore.Application.DTOs.Localization;

public sealed record LocalizationConfigDto
{
    private IReadOnlyList<string> _availableLanguages = Array.AsReadOnly(Array.Empty<string>());
    private IReadOnlyList<string> _enabledLanguages = Array.AsReadOnly(Array.Empty<string>());

    public string DefaultLanguage { get; init; } = "en";
    public string TmsProvider { get; init; } = "None";
    public string? TmsApiUrl { get; init; }
    public string? TmsProjectId { get; init; }
    public string? TmsComponent { get; init; }
    public bool IsConnected { get; init; }
    public IReadOnlyList<string> AvailableLanguages
    {
        get => _availableLanguages;
        init => _availableLanguages = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }

    public IReadOnlyList<string> EnabledLanguages
    {
        get => _enabledLanguages;
        init => _enabledLanguages = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
    public string FallbackLanguage { get; init; } = "en";
    public bool ClientPickerEnabled { get; init; } = true;
    public bool ForceOfflineMode { get; init; }
    public bool TmsApiKeyConfigured { get; init; }
}
