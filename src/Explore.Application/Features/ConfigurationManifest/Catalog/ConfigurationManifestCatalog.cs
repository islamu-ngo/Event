namespace Explore.Application.Features.ConfigurationManifest.Catalog;

using System.Collections.Frozen;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Settings;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;

public static class ConfigurationManifestCatalog
{
    private static readonly FrozenDictionary<string, ConfigurationManifestSettingCatalogEntry>
        TenantSettingEntries =
        CreateTenantSettings();

    private static readonly FrozenDictionary<string, ConfigurationManifestSettingCatalogEntry>
        InstanceSettingEntries =
        CreateInstanceSettings();

    private static readonly FrozenDictionary<string, ConfigurationManifestDocumentCatalogEntry>
        TenantDocumentEntries =
        CreateTenantDocuments();

    private static readonly FrozenDictionary<string, ConfigurationManifestDocumentCatalogEntry>
        InstanceDocumentEntries =
        CreateInstanceDocuments();

    public static IReadOnlyDictionary<string, ConfigurationManifestSettingCatalogEntry>
        TenantSettings => TenantSettingEntries;

    public static IReadOnlyDictionary<string, ConfigurationManifestSettingCatalogEntry>
        InstanceSettings => InstanceSettingEntries;

    public static IReadOnlyDictionary<string, ConfigurationManifestDocumentCatalogEntry>
        TenantDocuments => TenantDocumentEntries;

    public static IReadOnlyDictionary<string, ConfigurationManifestDocumentCatalogEntry>
        InstanceDocuments => InstanceDocumentEntries;

    public static bool TryGetTenantSetting(
        string key,
        out ConfigurationManifestSettingCatalogEntry? entry) =>
        TenantSettingEntries.TryGetValue(key, out entry);

    public static bool TryGetInstanceSetting(
        string key,
        out ConfigurationManifestSettingCatalogEntry? entry) =>
        InstanceSettingEntries.TryGetValue(key, out entry);

    public static bool TryGetTenantDocument(
        string key,
        out ConfigurationManifestDocumentCatalogEntry? entry) =>
        TenantDocumentEntries.TryGetValue(key, out entry);

    public static bool TryGetInstanceDocument(
        string key,
        out ConfigurationManifestDocumentCatalogEntry? entry) =>
        InstanceDocumentEntries.TryGetValue(key, out entry);

    private static FrozenDictionary<string, ConfigurationManifestSettingCatalogEntry>
        CreateTenantSettings()
    {
        ConfigurationManifestSettingCatalogEntry[] entries =
        [
            TenantSetting(TenantSettingDefinitions.WhiteLabelingEnabled),
            TenantSetting(EventReportingIntakeSettingDefinitions.IntakeEnabled),
            TenantSetting(EventSettingDefinitions.UserSubmissionEnabled),
            TenantSetting(EventSettingDefinitions.OrganizationSubmissionEnabled),
            TenantSetting(EventSettingDefinitions.GroupSubmissionEnabled),
            TenantSetting(EventSettingDefinitions.RequireApproval),
            TenantSetting(OrganizationSettingDefinitions.VerificationRequired),
            TenantSetting(OrganizationSettingDefinitions.SelfRegistrationEnabled),
            TenantSetting(GroupSettingDefinitions.SelfRegistrationEnabled),
            TenantSetting(ModuleSettingDefinitions.IslamicEnabled),
            TenantSetting(ModuleSettingDefinitions.TechEnabled),
            TenantSetting(RoutingSettingDefinitions.DefaultPublicHomePage),
            TenantSetting(AppearanceSettingDefinitions.DefaultThemeMode),
            TenantSetting(PublicExperienceSettingDefinitions.Mode),
            TenantSetting(
                PublicExperienceSettingDefinitions.EventCatalogLabel,
                maximumStringLength: 100)
        ];

        foreach (ConfigurationManifestSettingCatalogEntry entry in entries)
        {
            SettingDefinition? registered = SettingRegistry.Get(entry.Definition.Key);
            if (entry.Scope != ConfigurationManifestScope.Tenant
                || !ReferenceEquals(registered, entry.Definition)
                || entry.Definition.MinScope > SettingScope.Tenant
                || entry.Definition.MaxScope < SettingScope.Tenant
                || entry.Definition.IsSensitive)
            {
                throw new InvalidOperationException(
                    "The configuration manifest setting catalog contains an invalid definition.");
            }
        }

        string[] coordinatedKeys = entries
            .Where(entry => entry.Definition.RequiresCoordinatedMutation)
            .Select(entry => entry.Definition.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expectedCoordinatedKeys = PublicationPolicySettingKeys.All
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!coordinatedKeys.SequenceEqual(expectedCoordinatedKeys, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The configuration manifest publication-policy catalog is incomplete.");
        }

        return entries.ToFrozenDictionary(
            entry => entry.Definition.Key,
            StringComparer.Ordinal);
    }

    private static FrozenDictionary<string, ConfigurationManifestSettingCatalogEntry>
        CreateInstanceSettings()
    {
        ConfigurationManifestSettingCatalogEntry[] entries =
        [
            InstanceSetting(
                AppearanceSettingDefinitions.DefaultThemeMode),
            InstanceSetting(
                BrandingSettingDefinitions.CustomCssUrl,
                maximumStringLength: 2048),
            InstanceSetting(
                BrandingSettingDefinitions.DisplayName,
                maximumStringLength: 200),
            InstanceSetting(
                BrandingSettingDefinitions.FaviconUrl,
                maximumStringLength: 2048),
            InstanceSetting(
                BrandingSettingDefinitions.LogoUrl,
                maximumStringLength: 2048),
            InstanceSetting(EventSettingDefinitions.GroupSubmissionEnabled),
            InstanceSetting(EventSettingDefinitions.OrganizationSubmissionEnabled),
            InstanceSetting(EventSettingDefinitions.RequireApproval),
            InstanceSetting(EventSettingDefinitions.UserSubmissionEnabled),
            InstanceSetting(FooterSettingDefinitions.LockTenantCopyright),
            InstanceSetting(FooterSettingDefinitions.LockTenantDescription),
            InstanceSetting(FooterSettingDefinitions.LockTenantLinkGroups),
            InstanceSetting(FooterSettingDefinitions.LockTenantSocialLinks),
            InstanceSetting(FooterSettingDefinitions.LockTenantTemplate),
            InstanceSetting(GroupSettingDefinitions.SelfRegistrationEnabled),
            InstanceSetting(ModuleSettingDefinitions.IslamicEnabled),
            InstanceSetting(ModuleSettingDefinitions.TechEnabled),
            InstanceSetting(OrganizationSettingDefinitions.SelfRegistrationEnabled),
            InstanceSetting(OrganizationSettingDefinitions.TenantCanOmitVerification),
            InstanceSetting(OrganizationSettingDefinitions.VerificationRequired),
            InstanceSetting(
                PublicExperienceSettingDefinitions.EventCatalogLabel,
                maximumStringLength: 100),
            InstanceSetting(PublicExperienceSettingDefinitions.Mode),
            InstanceSetting(RoutingSettingDefinitions.DefaultPublicHomePage),
            InstanceSetting(TenantSettingDefinitions.SelfServiceRegistration),
            InstanceSetting(TenantSettingDefinitions.WhiteLabelingEnabled)
        ];

        foreach (ConfigurationManifestSettingCatalogEntry entry in entries)
        {
            SettingDefinition? registered = SettingRegistry.Get(entry.Definition.Key);
            if (entry.Scope != ConfigurationManifestScope.Instance
                || !ReferenceEquals(registered, entry.Definition)
                || entry.Definition.MinScope > SettingScope.Instance
                || entry.Definition.MaxScope < SettingScope.Instance
                || entry.Definition.IsSensitive)
            {
                throw new InvalidOperationException(
                    "The instance manifest setting catalog contains an invalid definition.");
            }
        }

        string[] coordinatedKeys = entries
            .Where(entry => entry.Definition.RequiresCoordinatedMutation)
            .Select(entry => entry.Definition.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expectedCoordinatedKeys =
        [
            EventSettingDefinitions.GroupSubmissionEnabled.Key,
            EventSettingDefinitions.OrganizationSubmissionEnabled.Key,
            EventSettingDefinitions.RequireApproval.Key,
            EventSettingDefinitions.UserSubmissionEnabled.Key
        ];
        if (!coordinatedKeys.SequenceEqual(
                expectedCoordinatedKeys.Order(StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The instance manifest publication-policy catalog is incomplete.");
        }

        return entries.ToFrozenDictionary(
            entry => entry.Definition.Key,
            StringComparer.Ordinal);
    }

    private static FrozenDictionary<string, ConfigurationManifestDocumentCatalogEntry>
        CreateTenantDocuments()
    {
        ConfigurationManifestDocumentCatalogEntry[] entries =
        [
            new(
                ConfigurationManifestScope.Tenant,
                SettingsDocumentKeys.Tenant.Branding,
                TenantBrandingSettingsDocumentDefaults.SchemaVersion,
                TenantBrandingSettingsDocumentDefaults.DefaultsVersion,
                typeof(BrandingSettings),
                ConfigurationManifestDocumentStorage.TenantSettingsDocument),
            new(
                ConfigurationManifestScope.Tenant,
                ConfigurationManifestDocumentKeys.TenantPaidEventPolicy,
                SchemaVersion: 1,
                DefaultsVersion: null,
                typeof(ConfigurationManifestPaidEventPolicyPayloadV1Alpha2),
                ConfigurationManifestDocumentStorage.PaidEventPolicy)
        ];

        return entries.ToFrozenDictionary(
            entry => entry.DocumentKey,
            StringComparer.Ordinal);
    }

    private static FrozenDictionary<string, ConfigurationManifestDocumentCatalogEntry>
        CreateInstanceDocuments()
    {
        ConfigurationManifestDocumentCatalogEntry[] entries =
        [
            new(
                ConfigurationManifestScope.Instance,
                ConfigurationManifestDocumentKeys.InstancePaidEventPolicy,
                SchemaVersion: 1,
                DefaultsVersion: null,
                typeof(ConfigurationManifestPaidEventPolicyPayloadV1Alpha2),
                ConfigurationManifestDocumentStorage.PaidEventPolicy)
        ];

        return entries.ToFrozenDictionary(
            entry => entry.DocumentKey,
            StringComparer.Ordinal);
    }

    private static ConfigurationManifestSettingCatalogEntry TenantSetting(
        SettingDefinition definition,
        int? maximumStringLength = null) =>
        new(
            ConfigurationManifestScope.Tenant,
            definition,
            maximumStringLength);

    private static ConfigurationManifestSettingCatalogEntry InstanceSetting(
        SettingDefinition definition,
        int? maximumStringLength = null) =>
        new(
            ConfigurationManifestScope.Instance,
            definition,
            maximumStringLength);
}
