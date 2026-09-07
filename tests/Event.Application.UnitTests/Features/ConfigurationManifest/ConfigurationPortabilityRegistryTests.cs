namespace Event.Application.UnitTests.Features.ConfigurationManifest;

using System.Collections;
using System.Reflection;
using Explore.Application.Settings;

public sealed class ConfigurationPortabilityRegistryTests
{
    private const string CatalogNamespace =
        "Explore.Application.Features.ConfigurationManifest.Catalog.";

    private static readonly Assembly ApplicationAssembly =
        typeof(SettingUpsertService).Assembly;

    private static readonly string[] RequiredSectionKeys =
    [
        "instance.settings",
        "instance.documents",
        "instance.legal_documents",
        "tenant.settings",
        "tenant.documents",
        "tenant.legal_documents",
        "tenant.footer",
        "tenant.navigation",
        "tenant.templates",
        "tenant.lookups",
        "tenant.custom_property_definitions",
        "tenant.localization",
        "tenant.registration_policy",
        "tenant.modules",
        "extensions",
        "excluded.secrets",
        "excluded.pii",
        "excluded.application_data",
        "excluded.operational_state",
        "excluded.provider_bindings",
        "excluded.deployment_topology"
    ];

    [Test]
    public async Task Registry_ContainsEveryGovernedAndExcludedSection()
    {
        IReadOnlyDictionary<string, object> sections = ReadSections();

        await Assert.That(sections.Keys.Order(StringComparer.Ordinal).ToArray())
            .IsEquivalentTo(RequiredSectionKeys.Order(StringComparer.Ordinal).ToArray());
    }

    [Test]
    public async Task Descriptors_DeclareCompletePortabilityBehavior()
    {
        IReadOnlyDictionary<string, object> sections = ReadSections();
        string[] requiredProperties =
        [
            "Key",
            "Scope",
            "Authority",
            "PortabilityClass",
            "SchemaVersion",
            "Dependencies",
            "References",
            "ArtifactKinds",
            "SupportsExport",
            "SupportsPreview",
            "SupportsDiff",
            "SupportsApply",
            "SupportsVerify",
            "SupportsRollback",
            "SupportsDeletion",
            "OmissionReasonCode",
            "TargetSetupRequirementCode",
            "DocumentationPath",
            "Owner"
        ];

        foreach ((string key, object descriptor) in sections)
        {
            PropertyInfo[] properties = descriptor.GetType().GetProperties(
                BindingFlags.Public | BindingFlags.Instance);
            string[] actual = properties.Select(property => property.Name).ToArray();

            await Assert.That(actual).IsEquivalentTo(requiredProperties);
            await Assert.That(ReadString(descriptor, "Key")).IsEqualTo(key);
            await Assert.That(ReadInt(descriptor, "SchemaVersion")).IsGreaterThan(0);
            await Assert.That(ReadString(descriptor, "Owner")).IsNotEmpty();
            await Assert.That(ReadString(descriptor, "DocumentationPath")).IsNotEmpty();
            await Assert.That(ReadEnumerable(descriptor, "Dependencies")).IsNotNull();
            await Assert.That(ReadEnumerable(descriptor, "References")).IsNotNull();
            await Assert.That(ReadEnumerable(descriptor, "ArtifactKinds")).IsNotNull();
        }
    }

    [Test]
    public async Task ExcludedCategories_CannotBecomePortableOrMutable()
    {
        IReadOnlyDictionary<string, object> sections = ReadSections();
        string[] excludedKeys = RequiredSectionKeys
            .Where(key => key.StartsWith("excluded.", StringComparison.Ordinal))
            .ToArray();

        foreach (string key in excludedKeys)
        {
            object descriptor = sections[key];
            await Assert.That(ReadString(descriptor, "PortabilityClass"))
                .IsNotEqualTo("Portable");
            await Assert.That(ReadBool(descriptor, "SupportsExport")).IsFalse();
            await Assert.That(ReadBool(descriptor, "SupportsPreview")).IsFalse();
            await Assert.That(ReadBool(descriptor, "SupportsDiff")).IsFalse();
            await Assert.That(ReadBool(descriptor, "SupportsApply")).IsFalse();
            await Assert.That(ReadBool(descriptor, "SupportsVerify")).IsFalse();
            await Assert.That(ReadBool(descriptor, "SupportsRollback")).IsFalse();
            await Assert.That(ReadBool(descriptor, "SupportsDeletion")).IsFalse();
            await Assert.That(ReadString(descriptor, "OmissionReasonCode")).IsNotEmpty();
        }
    }

    [Test]
    public async Task TenantPackageSections_NeverAdmitInstanceOrExcludedAuthority()
    {
        IReadOnlyDictionary<string, object> sections = ReadSections();

        foreach ((string key, object descriptor) in sections)
        {
            string[] artifactKinds = ReadEnumerable(descriptor, "ArtifactKinds")
                .Cast<object>()
                .Select(value => value.ToString()!)
                .ToArray();
            if (!artifactKinds.Contains(
                    "TenantConfigurationPackage",
                    StringComparer.Ordinal))
            {
                continue;
            }

            await Assert.That(key.StartsWith("instance.", StringComparison.Ordinal))
                .IsFalse();
            await Assert.That(key.StartsWith("excluded.", StringComparison.Ordinal))
                .IsFalse();
            await Assert.That(ReadString(descriptor, "Scope")).IsNotEqualTo("Instance");
            await Assert.That(ReadString(descriptor, "Authority"))
                .IsNotEqualTo("InstanceAdministrator");
        }
    }

    [Test]
    public async Task ManagedDeletion_IsNeverGrantedByOrdinaryPortability()
    {
        IReadOnlyDictionary<string, object> sections = ReadSections();

        foreach (object descriptor in sections.Values)
        {
            if (ReadBool(descriptor, "SupportsDeletion"))
            {
                await Assert.That(ReadString(descriptor, "PortabilityClass"))
                    .IsEqualTo("Managed");
            }
        }
    }

    private static IReadOnlyDictionary<string, object> ReadSections()
    {
        Type registryType = ApplicationAssembly.GetType(
            CatalogNamespace + "ConfigurationPortabilityRegistry")
            ?? throw new InvalidOperationException(
                "The ConfigurationPortabilityRegistry contract is missing.");
        PropertyInfo sectionsProperty = registryType.GetProperty(
            "Sections",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "The ConfigurationPortabilityRegistry.Sections contract is missing.");
        var entries = sectionsProperty.GetValue(null) as IEnumerable
            ?? throw new InvalidOperationException(
                "The ConfigurationPortabilityRegistry.Sections contract is not enumerable.");
        var sections = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (object entry in entries)
        {
            Type entryType = entry.GetType();
            string key = entryType.GetProperty("Key")?.GetValue(entry) as string
                ?? throw new InvalidOperationException(
                    "A portability-registry entry has no string key.");
            object value = entryType.GetProperty("Value")?.GetValue(entry)
                ?? throw new InvalidOperationException(
                    "A portability-registry entry has no descriptor.");
            sections.Add(key, value);
        }

        return sections;
    }

    private static object ReadValue(object descriptor, string propertyName) =>
        descriptor.GetType().GetProperty(propertyName)?.GetValue(descriptor)
        ?? throw new InvalidOperationException(
            $"Missing portability descriptor property '{propertyName}'.");

    private static string ReadString(object descriptor, string propertyName) =>
        ReadValue(descriptor, propertyName).ToString()!;

    private static bool ReadBool(object descriptor, string propertyName) =>
        (bool)ReadValue(descriptor, propertyName);

    private static int ReadInt(object descriptor, string propertyName) =>
        (int)ReadValue(descriptor, propertyName);

    private static IEnumerable ReadEnumerable(object descriptor, string propertyName) =>
        ReadValue(descriptor, propertyName) as IEnumerable
        ?? throw new InvalidOperationException(
            $"Portability descriptor property '{propertyName}' is not enumerable.");
}
