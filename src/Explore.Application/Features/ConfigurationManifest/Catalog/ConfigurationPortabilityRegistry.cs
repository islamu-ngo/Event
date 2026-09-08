namespace Explore.Application.Features.ConfigurationManifest.Catalog;

using System.Collections.Frozen;

public enum ConfigurationPortabilityScope
{
    Instance,
    Tenant,
    Shared,
    Excluded
}

public enum ConfigurationPortabilityAuthority
{
    None,
    InstanceAdministrator,
    TenantAdministrator,
    InstanceOrTenantAdministrator
}

public enum ConfigurationPortabilityClass
{
    Portable,
    PortableWithMapping,
    Managed,
    Secret,
    PersonallyIdentifiableInformation,
    ApplicationData,
    OperationalState,
    ProviderBinding,
    DeploymentTopology
}

public enum ConfigurationPortabilityArtifactKind
{
    ConfigurationManifest,
    TenantConfigurationPackage
}

public sealed record ConfigurationPortabilitySectionDescriptor(
    string Key,
    ConfigurationPortabilityScope Scope,
    ConfigurationPortabilityAuthority Authority,
    ConfigurationPortabilityClass PortabilityClass,
    int SchemaVersion,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> References,
    IReadOnlyList<ConfigurationPortabilityArtifactKind> ArtifactKinds,
    bool SupportsExport,
    bool SupportsPreview,
    bool SupportsDiff,
    bool SupportsApply,
    bool SupportsVerify,
    bool SupportsRollback,
    bool SupportsDeletion,
    string OmissionReasonCode,
    string TargetSetupRequirementCode,
    string DocumentationPath,
    string Owner);

public static class ConfigurationPortabilityRegistry
{
    private static readonly IReadOnlyList<string> NoDependencies = Array.Empty<string>();
    private static readonly IReadOnlyList<string> NoReferences = Array.Empty<string>();
    private static readonly IReadOnlyList<ConfigurationPortabilityArtifactKind>
        ManifestOnly =
        [
            ConfigurationPortabilityArtifactKind.ConfigurationManifest
        ];
    private static readonly IReadOnlyList<ConfigurationPortabilityArtifactKind>
        ManifestAndTenantPackage =
        [
            ConfigurationPortabilityArtifactKind.ConfigurationManifest,
            ConfigurationPortabilityArtifactKind.TenantConfigurationPackage
        ];
    private static readonly IReadOnlyList<ConfigurationPortabilityArtifactKind>
        NoArtifacts = Array.Empty<ConfigurationPortabilityArtifactKind>();

    private static readonly FrozenDictionary<string, ConfigurationPortabilitySectionDescriptor>
        SectionEntries = CreateSections();

    public static IReadOnlyDictionary<string, ConfigurationPortabilitySectionDescriptor>
        Sections => SectionEntries;

    private static FrozenDictionary<string, ConfigurationPortabilitySectionDescriptor>
        CreateSections()
    {
        ConfigurationPortabilitySectionDescriptor[] entries =
        [
            PortableInstance("instance.settings", "Settings"),
            PortableInstance("instance.documents", "Typed documents"),
            PortableInstance("instance.legal_documents", "Legal documents"),
            PortableTenant("tenant.settings", "Settings"),
            PortableTenant("tenant.documents", "Typed documents"),
            PortableTenant("tenant.legal_documents", "Legal documents"),
            UnavailableTenant("tenant.footer", "Footer"),
            UnavailableTenant("tenant.navigation", "Navigation"),
            UnavailableTenant("tenant.templates", "Templates"),
            UnavailableTenant("tenant.lookups", "Lookup configuration"),
            UnavailableTenant(
                "tenant.custom_property_definitions",
                "Custom property definitions"),
            UnavailableTenant("tenant.localization", "Localization"),
            UnavailableTenant(
                "tenant.registration_policy",
                "Registration policy"),
            UnavailableTenant("tenant.modules", "Module governance"),
            new(
                "extensions",
                ConfigurationPortabilityScope.Shared,
                ConfigurationPortabilityAuthority.InstanceOrTenantAdministrator,
                ConfigurationPortabilityClass.PortableWithMapping,
                1,
                NoDependencies,
                NoReferences,
                ManifestAndTenantPackage,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                "configuration_portability_extension_pack_required",
                "configuration_portability_extension_pack_required",
                "docs/internal/EXTENSIBILITY.md",
                "Extension registry"),
            Excluded(
                "excluded.secrets",
                ConfigurationPortabilityClass.Secret,
                "configuration_portability_secret_omitted",
                "Secrets"),
            Excluded(
                "excluded.pii",
                ConfigurationPortabilityClass.PersonallyIdentifiableInformation,
                "configuration_portability_pii_omitted",
                "Privacy"),
            Excluded(
                "excluded.application_data",
                ConfigurationPortabilityClass.ApplicationData,
                "configuration_portability_application_data_omitted",
                "Domain data"),
            Excluded(
                "excluded.operational_state",
                ConfigurationPortabilityClass.OperationalState,
                "configuration_portability_operational_state_omitted",
                "Operations"),
            Excluded(
                "excluded.provider_bindings",
                ConfigurationPortabilityClass.ProviderBinding,
                "configuration_portability_provider_binding_omitted",
                "Integrations"),
            Excluded(
                "excluded.deployment_topology",
                ConfigurationPortabilityClass.DeploymentTopology,
                "configuration_portability_deployment_topology_omitted",
                "Deployment")
        ];

        return entries.ToFrozenDictionary(entry => entry.Key, StringComparer.Ordinal);
    }

    private static ConfigurationPortabilitySectionDescriptor PortableInstance(
        string key,
        string owner) =>
        Portable(
            key,
            ConfigurationPortabilityScope.Instance,
            ConfigurationPortabilityAuthority.InstanceAdministrator,
            ConfigurationPortabilityClass.Portable,
            ManifestOnly,
            owner);

    private static ConfigurationPortabilitySectionDescriptor PortableTenant(
        string key,
        string owner) =>
        Portable(
            key,
            ConfigurationPortabilityScope.Tenant,
            ConfigurationPortabilityAuthority.TenantAdministrator,
            ConfigurationPortabilityClass.Portable,
            ManifestAndTenantPackage,
            owner);

    private static ConfigurationPortabilitySectionDescriptor UnavailableTenant(
        string key,
        string owner) =>
        new(
            key,
            ConfigurationPortabilityScope.Tenant,
            ConfigurationPortabilityAuthority.TenantAdministrator,
            ConfigurationPortabilityClass.PortableWithMapping,
            1,
            NoDependencies,
            NoReferences,
            NoArtifacts,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            "configuration_portability_section_not_serialized",
            "configuration_portability_section_not_serialized",
            "docs/internal/CONFIGURATION_MANIFEST.md#section-coverage",
            owner);

    private static ConfigurationPortabilitySectionDescriptor Portable(
        string key,
        ConfigurationPortabilityScope scope,
        ConfigurationPortabilityAuthority authority,
        ConfigurationPortabilityClass portabilityClass,
        IReadOnlyList<ConfigurationPortabilityArtifactKind> artifactKinds,
        string owner) =>
        new(
            key,
            scope,
            authority,
            portabilityClass,
            1,
            NoDependencies,
            NoReferences,
            artifactKinds,
            true,
            true,
            true,
            true,
            true,
            true,
            false,
            string.Empty,
            string.Empty,
            "docs/internal/CONFIGURATION_MANIFEST.md",
            owner);

    private static ConfigurationPortabilitySectionDescriptor Excluded(
        string key,
        ConfigurationPortabilityClass portabilityClass,
        string omissionReasonCode,
        string owner) =>
        new(
            key,
            ConfigurationPortabilityScope.Excluded,
            ConfigurationPortabilityAuthority.None,
            portabilityClass,
            1,
            NoDependencies,
            NoReferences,
            NoArtifacts,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            omissionReasonCode,
            omissionReasonCode,
            "docs/internal/CONFIGURATION_MANIFEST.md#excluded-authority",
            owner);
}
