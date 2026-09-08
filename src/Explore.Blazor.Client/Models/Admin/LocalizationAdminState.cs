using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Localization;

namespace Explore.Blazor.Client.Models.Admin;

public sealed class LocalizationAdminState
{
    public string DefaultLanguage { get; set; } = "en";
    public string TmsProvider { get; set; } = "none";
    public string? TmsApiUrl { get; set; }
    public string? TmsProjectId { get; set; }
    public string? TmsComponent { get; set; }
    public List<string> EnabledLanguages { get; set; } = new() { "en", "fr", "ar" };
    public string FallbackLanguage { get; set; } = "en";
    public bool ClientPickerEnabled { get; set; } = true;
    public bool ForceOfflineMode { get; set; }

    public bool TmsApiKeyConfigured { get; set; }
    public string? TmsApiKey { get; set; }

    public bool IsLoading { get; set; }
    public bool IsSaving { get; set; }
    public bool IsTesting { get; set; }
    public bool IsSavingKillSwitches { get; set; }
    public string? LastTestResult { get; set; }
    public bool LastTestSucceeded { get; set; }

    public bool BundlePathWritable { get; set; }
    public string? BundlePathReason { get; set; }
    public string? BundlePathTarget { get; set; }
    public bool BundleHealthLoaded { get; set; }
    public Dictionary<string, bool> ExportingLanguages { get; set; } = new();

    public bool IsOffline => string.Equals(TmsProvider, "none", StringComparison.OrdinalIgnoreCase);
    public bool IsTolgee => string.Equals(TmsProvider, "tolgee", StringComparison.OrdinalIgnoreCase);
    public bool IsWeblate => string.Equals(TmsProvider, "weblate", StringComparison.OrdinalIgnoreCase);

    public void LoadFrom(LocalizationConfigDto config)
    {
        DefaultLanguage = CultureRegistry.Contains(config.DefaultLanguage) ? config.DefaultLanguage : "en";
        TmsProvider = (config.TmsProvider ?? "None").Trim().ToLowerInvariant();
        TmsApiUrl = config.TmsApiUrl;
        TmsProjectId = config.TmsProjectId;
        TmsComponent = config.TmsComponent;

        if (config.EnabledLanguages is { Count: > 0 })
        {
            EnabledLanguages = config.EnabledLanguages
                .Where(CultureRegistry.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (EnabledLanguages.Count == 0)
        {
            EnabledLanguages = [DefaultLanguage];
        }

        FallbackLanguage = CultureRegistry.Contains(config.FallbackLanguage)
            ? config.FallbackLanguage!
            : DefaultLanguage;
        ClientPickerEnabled = config.ClientPickerEnabled ?? true;
        ForceOfflineMode = config.ForceOfflineMode ?? false;
        TmsApiKeyConfigured = config.TmsApiKeyConfigured ?? false;
    }

    public UpdateLocalizationGovernanceDto ToPayload() => new()
    {
        Tms = new LocalizationTmsUpdateDto
        {
            Provider = TmsProvider,
            ApiUrl = TmsApiUrl,
            ProjectId = TmsProjectId,
            Component = TmsComponent
        },
        Languages = new LocalizationLanguagePolicyUpdateDto
        {
            DefaultLanguage = DefaultLanguage,
            EnabledLanguages = EnabledLanguages.ToArray(),
            FallbackLanguage = FallbackLanguage
        },
        Runtime = new LocalizationRuntimeUpdateDto
        {
            ClientPickerEnabled = ClientPickerEnabled,
            ForceOfflineMode = ForceOfflineMode
        }
    };

    public string? ValidateSynchronously()
    {
        if (EnabledLanguages.Count == 0)
            return "At least one language must be enabled.";
        if (!EnabledLanguages.Contains(DefaultLanguage, StringComparer.OrdinalIgnoreCase))
            return "Default language must be one of the enabled languages.";
        if (!EnabledLanguages.Contains(FallbackLanguage, StringComparer.OrdinalIgnoreCase))
            return "Fallback language must be one of the enabled languages.";
        if (!IsOffline && string.IsNullOrWhiteSpace(TmsApiUrl))
            return "TMS API URL is required when a TMS provider is selected.";
        if (!IsOffline && string.IsNullOrWhiteSpace(TmsProjectId))
            return "TMS Project ID is required when a TMS provider is selected.";
        if (IsWeblate && string.IsNullOrWhiteSpace(TmsComponent))
            return "Weblate requires a component slug.";
        return null;
    }
}
