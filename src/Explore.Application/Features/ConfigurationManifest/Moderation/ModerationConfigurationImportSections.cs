using System.Text.Json;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

namespace Explore.Application.Features.ConfigurationManifest.Moderation;

/// <summary>
/// Native import sections do not advertise wire registry capabilities.
/// </summary>
public static class ModerationConfigurationImportSections
{
    public const string Instance = "instance.moderation";
    public const string Tenant = "tenant.moderation";

    public static bool IsModeration(string section) => section is Instance or Tenant;
    public static string ForTarget(ConfigurationImportTarget target) =>
        target.Scope == ConfigurationImportScope.Instance ? Instance : Tenant;

    public static ModerationConfigurationDocument? Read(
        ConfigurationImportTarget target, ReadOnlyMemory<byte> artifact, ConfigurationImportArtifactParser parser)
    {
        var wire = target.Scope == ConfigurationImportScope.Instance
            ? parser.Parse(artifact).Manifest.Spec.Instance.Moderation
            : parser.ParseTenantPackage(artifact).Package.Spec.Moderation;
        return wire is null ? null : FromWire(wire);
    }

    public static ModerationConfigurationDocument FromWire(ConfigurationModerationDocumentV1 wire) =>
        ModerationConfigurationDocumentProjection.Parse(JsonSerializer.SerializeToUtf8Bytes(
            wire, ConfigurationPortabilityJsonContext.Default.ConfigurationModerationDocumentV1));

    public static ConfigurationModerationDocumentV1 ToWire(ModerationConfigurationDocument document) =>
        JsonSerializer.Deserialize(ModerationConfigurationDocumentProjection.Serialize(document),
            ConfigurationPortabilityJsonContext.Default.ConfigurationModerationDocumentV1)
        ?? throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.ContractInvalid);

    public static ConfigurationImportSectionSnapshot Snapshot(string key, ModerationConfigurationDocument document) =>
        new(key, Digest(document), ConfigurationPortabilityClass.Portable, supportsPreview: true,
            supportsDiff: true, requiresExternalSetup: false);

    public static string Digest(ModerationConfigurationDocument document) =>
        ConfigurationImportDigest.ComputeBytes(ModerationConfigurationDocumentProjection.Serialize(document));

    public static IReadOnlyList<ModerationConfigurationImportSource> ReadSelected(
        ConfigurationImportTarget target, ReadOnlyMemory<byte> artifact, ConfigurationImportPreviewRequest request)
    {
        var parser = new ConfigurationImportArtifactParser();
        var result = new List<ModerationConfigurationImportSource>();
        if (target.Scope == ConfigurationImportScope.Instance)
        {
            var manifest = parser.Parse(artifact).Manifest;
            if (request.SelectedSectionKeys.Contains(Instance, StringComparer.Ordinal))
                result.Add(new(Instance, "instance", "instance", FromWire(manifest.Spec.Instance.Moderation
                    ?? throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.ApplyBlocked))));
            if (request.SelectedSectionKeys.Contains(Tenant, StringComparer.Ordinal))
            {
                foreach (var tenant in manifest.Spec.Tenants.Where(tenant => tenant.Spec.Moderation is not null))
                    result.Add(new(Tenant, tenant.Metadata.Name, ResolveTargetName(tenant.Metadata.Name, request.Mappings),
                        FromWire(tenant.Spec.Moderation!)));
                if (!result.Any(source => source.SectionKey == Tenant))
                    throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.ApplyBlocked);
            }
        }
        else if (request.SelectedSectionKeys.Contains(Tenant, StringComparer.Ordinal))
        {
            var package = parser.ParseTenantPackage(artifact).Package;
            result.Add(new(Tenant, package.Metadata.Source.TenantName, target.TenantId!.Value.ToString("N"),
                FromWire(package.Spec.Moderation
                    ?? throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.ApplyBlocked))));
        }
        if (result.Count > ModerationConfigurationImportReservationSet.MaximumTargets
            || result.Select(source => (source.SectionKey, source.TargetName)).Distinct().Count() != result.Count)
            throw new ModerationConfigurationImportException(Contracts.Persistence.ModerationPolicyPublicationOutcome.Conflict);
        return result;
    }

    private static string ResolveTargetName(string source, IReadOnlyDictionary<string, string> mappings)
    {
        var mapped = mappings.TryGetValue(source, out var direct) ? direct
            : mappings.TryGetValue($"tenant:{source}", out var qualified) ? qualified : source;
        return mapped.StartsWith("tenant:", StringComparison.Ordinal) ? mapped["tenant:".Length..] : mapped;
    }

    public static ConfigurationImportSectionSnapshot TenantSnapshot(
        IEnumerable<KeyValuePair<string, ModerationConfigurationDocument>> documents) =>
        new(Tenant, ConfigurationImportDigest.Compute(documents.Select(pair => $"{pair.Key}\u001f{Digest(pair.Value)}")),
            ConfigurationPortabilityClass.Portable, true, true, false);
}
