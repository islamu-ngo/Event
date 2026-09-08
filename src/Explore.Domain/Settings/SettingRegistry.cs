// ABOUTME: Central registry of all setting definitions in the platform.
// ABOUTME: Static, code-defined — avoids DB bootstrapping issues and ensures compile-time validation.

namespace Explore.Domain.Settings;

using System.Collections.Frozen;
using Explore.Domain.Settings.Definitions;

/// <summary>
/// Central registry of all setting definitions.
/// Populated at static initialization from per-category definition classes.
/// Thread-safe and immutable after initialization.
/// </summary>
public static class SettingRegistry
{
    private static readonly FrozenDictionary<string, SettingDefinition> DefinitionsByKey;
    private static readonly FrozenDictionary<string, IReadOnlyList<SettingDefinition>> ByCategory;

    static SettingRegistry()
    {
        var all = new List<SettingDefinition>();

        all.AddRange(DeploymentSettingDefinitions.All);
        all.AddRange(TenantSettingDefinitions.All);
        all.AddRange(AdminPortalSettingDefinitions.All);
        all.AddRange(RoutingSettingDefinitions.All);
        all.AddRange(EventSettingDefinitions.All);
        all.AddRange(EventReportingIntakeSettingDefinitions.All);
        all.AddRange(OrganizationSettingDefinitions.All);
        all.AddRange(GroupSettingDefinitions.All);
        all.AddRange(ModuleSettingDefinitions.All);
        all.AddRange(BrandingSettingDefinitions.All);
        all.AddRange(AppearanceSettingDefinitions.All);
        all.AddRange(DomainSettingDefinitions.All);
        all.AddRange(EmailSettingDefinitions.All);
        all.AddRange(WebhookDeliverySettingDefinitions.All);
        all.AddRange(StorageSettingDefinitions.All);
        all.AddRange(SecuritySettingDefinitions.All);
        all.AddRange(AuthenticationSettingDefinitions.All);
        all.AddRange(SupportAccessSettingDefinitions.All);
        all.AddRange(CerbosSettingDefinitions.All);
        all.AddRange(ReportingSettingDefinitions.All);
        all.AddRange(IntegrationSettingDefinitions.All);
        all.AddRange(AnalyticsSettingDefinitions.All);
        all.AddRange(McpSettingDefinitions.All);
        all.AddRange(AtprotoFederationSettingDefinitions.All);
        all.AddRange(LocationPrivacySettingDefinitions.All);
        all.AddRange(AddressGovernanceSettingDefinitions.All);
        all.AddRange(AiAssistantSettingDefinitions.All);
        all.AddRange(AiAssistantPreferenceSettingDefinitions.All);
        all.AddRange(UiShellSettingDefinitions.All);
        all.AddRange(TenantDelegationSettingDefinitions.All);
        all.AddRange(FooterSettingDefinitions.All);
        all.AddRange(EventListSettingDefinitions.All);
        all.AddRange(NotificationSettingDefinitions.All);
        all.AddRange(CustomPropertyQuotaSettingDefinitions.All);
        all.AddRange(LocalizationSettingDefinitions.All);
        all.AddRange(PublicExperienceSettingDefinitions.All);
        all.AddRange(AnonymousRegistrationChallengeSettingDefinitions.All);

        DefinitionsByKey = all.ToFrozenDictionary(d => d.Key);
        ByCategory = all
            .GroupBy(d => d.Category)
            .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<SettingDefinition>)g.ToList().AsReadOnly());
    }

    /// <summary>Gets a setting definition by key, or null if not registered.</summary>
    public static SettingDefinition? Get(string key) =>
        DefinitionsByKey.GetValueOrDefault(key);

    /// <summary>Gets all definitions in a category.</summary>
    public static IReadOnlyList<SettingDefinition> GetByCategory(string category) =>
        ByCategory.GetValueOrDefault(category) ?? [];

    /// <summary>All registered category names.</summary>
    public static IReadOnlyCollection<string> AllCategories => ByCategory.Keys;

    /// <summary>All registered setting definitions.</summary>
    public static IReadOnlyCollection<SettingDefinition> All => DefinitionsByKey.Values;

    /// <summary>Total count of registered definitions.</summary>
    public static int Count => DefinitionsByKey.Count;

    /// <summary>Returns true if the given key is registered.</summary>
    public static bool Contains(string key) => DefinitionsByKey.ContainsKey(key);
}
