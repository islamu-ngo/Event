namespace Explore.Application.Features.ConfigurationManifest.Importing;

using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public static class ConfigurationImportArtifactSnapshotFactory
{
    public static ImmutableArray<ConfigurationImportSectionSnapshot> FromManifest(
        ConfigurationManifestV1Alpha2 manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest = manifest with
        {
            Spec = manifest.Spec with
            {
                Instance = manifest.Spec.Instance with
                {
                    LegalDocuments = NormalizeLegalDocuments(manifest.Spec.Instance.LegalDocuments)
                },
                Tenants = manifest.Spec.Tenants.Select(tenant => tenant with
                {
                    Spec = tenant.Spec with { LegalDocuments = NormalizeLegalDocuments(tenant.Spec.LegalDocuments) }
                }).ToArray()
            }
        };
        JsonElement root = JsonSerializer.SerializeToElement(
            manifest,
            ConfigurationPortabilityJsonContext.Default.ConfigurationManifestV1Alpha2);
        JsonElement spec = root.GetProperty("spec");
        JsonElement instance = spec.GetProperty("instance");
        JsonElement tenants = spec.GetProperty("tenants");

        return
        [
            .. ModerationSection(ModerationConfigurationImportSections.Instance, manifest.Spec.Instance.Moderation),
            .. manifest.Spec.Tenants.Any(tenant => tenant.Spec.Moderation is not null)
                ? new[] { ModerationConfigurationImportSections.TenantSnapshot(manifest.Spec.Tenants
                    .Where(tenant => tenant.Spec.Moderation is not null)
                    .Select(tenant => KeyValuePair.Create(tenant.Metadata.Name,
                        ModerationConfigurationImportSections.FromWire(tenant.Spec.Moderation!)))) }
                : [],
            Section("instance.settings", instance.GetProperty("settings")),
            Section("instance.documents", instance.GetProperty("documents")),
            Section(
                "instance.legal_documents",
                instance.GetProperty("legalDocuments")),
            TenantProjection("tenant.settings", tenants, "settings"),
            TenantProjection("tenant.documents", tenants, "documents"),
            TenantProjection(
                "tenant.legal_documents",
                tenants,
                "legalDocuments")
        ];
    }

    public static ImmutableArray<ConfigurationImportSectionSnapshot>
        FromManifestTenant(
        ConfigurationManifestV1Alpha2 manifest,
        string tenantName)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantName);
        ConfigurationManifestTenantV1Alpha2 tenant = manifest.Spec.Tenants
            .SingleOrDefault(candidate => string.Equals(
                candidate.Metadata.Name,
                tenantName,
                StringComparison.Ordinal))
            ?? throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.TargetMismatch);
        tenant = tenant with
        {
            Spec = tenant.Spec with { LegalDocuments = NormalizeLegalDocuments(tenant.Spec.LegalDocuments) }
        };
        JsonElement root = JsonSerializer.SerializeToElement(
            tenant,
            ConfigurationPortabilityJsonContext.Default
                .ConfigurationManifestTenantV1Alpha2);
        JsonElement spec = root.GetProperty("spec");

        return
        [
            .. ModerationSection(ModerationConfigurationImportSections.Tenant, tenant.Spec.Moderation),
            TenantSettingsSection(spec),
            Section("tenant.documents", spec.GetProperty("documents")),
            Section(
                "tenant.legal_documents",
                spec.GetProperty("legalDocuments"))
        ];
    }

    public static ImmutableArray<ConfigurationImportSectionSnapshot>
        FromTenantPackage(
        TenantConfigurationPackageV1Alpha2 package)
    {
        ArgumentNullException.ThrowIfNull(package);
        package = package with
        {
            Spec = package.Spec with { LegalDocuments = NormalizeLegalDocuments(package.Spec.LegalDocuments) }
        };
        JsonElement root = JsonSerializer.SerializeToElement(
            package,
            ConfigurationPortabilityJsonContext.Default
                .TenantConfigurationPackageV1Alpha2);
        JsonElement spec = root.GetProperty("spec");

        return
        [
            .. ModerationSection(ModerationConfigurationImportSections.Tenant, package.Spec.Moderation),
            TenantSettingsSection(spec),
            Section("tenant.documents", spec.GetProperty("documents")),
            Section(
                "tenant.legal_documents",
                spec.GetProperty("legalDocuments"))
        ];
    }

    private static IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> NormalizeLegalDocuments(
        IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> documents) =>
        documents.ToDictionary(pair => pair.Key,
            pair => LegalConfigurationSnapshotProjection.NormalizeDraft(pair.Value), StringComparer.Ordinal);

    public static string RevisionDigest(
        IEnumerable<ConfigurationImportSectionSnapshot> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        return ConfigurationImportDigest.Compute(sections.Select(section =>
            $"{section.SectionKey}\u001f{section.NormalizedDigest}"));
    }

    private static IEnumerable<ConfigurationImportSectionSnapshot> ModerationSection(
        string key, ConfigurationModerationDocumentV1? document) =>
        document is null ? [] : [ModerationConfigurationImportSections.Snapshot(
            key, ModerationConfigurationImportSections.FromWire(document))];

    private static ConfigurationImportSectionSnapshot TenantProjection(
        string sectionKey,
        JsonElement tenants,
        string propertyName)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (JsonElement tenant in tenants.EnumerateArray()
                         .OrderBy(
                             value => value.GetProperty("metadata")
                                 .GetProperty("name").GetString(),
                             StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString(
                    "name",
                    tenant.GetProperty("metadata")
                        .GetProperty("name")
                        .GetString());
                writer.WritePropertyName("value");
                JsonElement spec = tenant.GetProperty("spec");
                if (string.Equals(
                        propertyName,
                        "settings",
                        StringComparison.Ordinal))
                {
                    WriteTenantSettings(writer, spec);
                }
                else
                {
                    WriteNormalized(writer, spec.GetProperty(propertyName));
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        return Section(sectionKey, buffer.WrittenSpan,
            propertyName == "legalDocuments" && tenants.EnumerateArray().Any(tenant =>
                HasUnsupportedLegalMetadata(tenant.GetProperty("spec").GetProperty(propertyName)))
                ? "configuration_import_legal_metadata_not_supported" : null);
    }

    private static ConfigurationImportSectionSnapshot Section(
        string sectionKey,
        JsonElement value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteNormalized(writer, value);
        }
        return Section(sectionKey, buffer.WrittenSpan,
            sectionKey.EndsWith(".legal_documents", StringComparison.Ordinal)
                && HasUnsupportedLegalMetadata(value)
                ? "configuration_import_legal_metadata_not_supported" : null);
    }

    private static bool HasUnsupportedLegalMetadata(JsonElement documents) =>
        documents.EnumerateObject().Any(document =>
            document.Value.TryGetProperty("jurisdictionAssumptions", out var assumptions)
                && assumptions.GetArrayLength() != 0
            || document.Value.TryGetProperty("changeSummary", out var summary)
                && summary.ValueKind != JsonValueKind.Null);

    private static ConfigurationImportSectionSnapshot TenantSettingsSection(
        JsonElement spec)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteTenantSettings(writer, spec);
        }
        return Section("tenant.settings", buffer.WrittenSpan);
    }

    private static void WriteTenantSettings(
        Utf8JsonWriter writer,
        JsonElement spec)
    {
        writer.WriteStartObject();
        writer.WriteString(
            "displayName",
            spec.GetProperty("displayName").GetString());
        writer.WritePropertyName("settings");
        WriteNormalized(writer, spec.GetProperty("settings"));
        writer.WriteEndObject();
    }

    private static ConfigurationImportSectionSnapshot Section(
        string sectionKey,
        ReadOnlySpan<byte> normalizedBytes,
        string? blockingReasonCode = null)
    {
        ConfigurationPortabilitySectionDescriptor descriptor =
            ConfigurationPortabilityRegistry.Sections[sectionKey];
        return new ConfigurationImportSectionSnapshot(
            sectionKey,
            ConfigurationImportDigest.ComputeBytes(normalizedBytes),
            descriptor.PortabilityClass,
            descriptor.SupportsPreview,
            descriptor.SupportsDiff,
            requiresExternalSetup: false,
            blockingReasonCode: blockingReasonCode);
    }

    private static void WriteNormalized(
        Utf8JsonWriter writer,
        JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in value.EnumerateObject()
                             .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteNormalized(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in value.EnumerateArray())
                {
                    WriteNormalized(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                if (value.TryGetInt64(out long integer))
                    writer.WriteNumberValue(integer);
                else if (value.TryGetDecimal(out decimal decimalValue))
                    writer.WriteRawValue(
                        decimalValue.ToString(
                            "G29",
                            CultureInfo.InvariantCulture),
                        skipInputValidation: true);
                else
                    writer.WriteNumberValue(value.GetDouble());
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
