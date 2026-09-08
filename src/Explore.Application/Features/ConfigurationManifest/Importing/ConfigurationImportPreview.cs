namespace Explore.Application.Features.ConfigurationManifest.Importing;

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public enum ConfigurationImportPreviewCategory
{
    Changed = 1,
    Unchanged = 2,
    Skipped = 3,
    Mapped = 4,
    Blocking = 5,
    Warning = 6,
    Omitted = 7,
    ExternalSetupRequired = 8
}

public sealed record ConfigurationImportPreviewBinding
{
    public ConfigurationImportPreviewBinding(
        ConfigurationImportTarget target,
        string artifactDigest,
        string targetRevisionDigest,
        string selectedSectionsDigest,
        string mappingDigest,
        ConfigurationImportApplyMode applyMode,
        string requiredApprovalDigest,
        DateTime expiresAt)
    {
        ArgumentNullException.ThrowIfNull(target);
        ConfigurationImportContractGuard.ValidateDigest(
            artifactDigest,
            nameof(artifactDigest));
        ConfigurationImportContractGuard.ValidateDigest(
            targetRevisionDigest,
            nameof(targetRevisionDigest));
        ConfigurationImportContractGuard.ValidateDigest(
            selectedSectionsDigest,
            nameof(selectedSectionsDigest));
        ConfigurationImportContractGuard.ValidateDigest(
            mappingDigest,
            nameof(mappingDigest));
        ConfigurationImportContractGuard.ValidateDigest(
            requiredApprovalDigest,
            nameof(requiredApprovalDigest));
        if (!Enum.IsDefined(applyMode))
            throw new ArgumentOutOfRangeException(nameof(applyMode));
        ConfigurationImportContractGuard.RequireUtc(expiresAt, nameof(expiresAt));

        Target = target;
        ArtifactDigest = artifactDigest;
        TargetRevisionDigest = targetRevisionDigest;
        SelectedSectionsDigest = selectedSectionsDigest;
        MappingDigest = mappingDigest;
        ApplyMode = applyMode;
        RequiredApprovalDigest = requiredApprovalDigest;
        ExpiresAt = expiresAt;
    }

    public ConfigurationImportTarget Target { get; }
    public string ArtifactDigest { get; }
    public string TargetRevisionDigest { get; }
    public string SelectedSectionsDigest { get; }
    public string MappingDigest { get; }
    public ConfigurationImportApplyMode ApplyMode { get; }
    public string RequiredApprovalDigest { get; }
    public DateTime ExpiresAt { get; }

    public bool Matches(ConfigurationImportPreviewBinding candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Target == candidate.Target
            && string.Equals(
                ArtifactDigest,
                candidate.ArtifactDigest,
                StringComparison.Ordinal)
            && string.Equals(
                TargetRevisionDigest,
                candidate.TargetRevisionDigest,
                StringComparison.Ordinal)
            && string.Equals(
                SelectedSectionsDigest,
                candidate.SelectedSectionsDigest,
                StringComparison.Ordinal)
            && string.Equals(
                MappingDigest,
                candidate.MappingDigest,
                StringComparison.Ordinal)
            && ApplyMode == candidate.ApplyMode
            && string.Equals(
                RequiredApprovalDigest,
                candidate.RequiredApprovalDigest,
                StringComparison.Ordinal)
            && ExpiresAt == candidate.ExpiresAt;
    }
}

public sealed record ConfigurationImportSectionSnapshot
{
    public ConfigurationImportSectionSnapshot(
        string sectionKey,
        string canonicalDigest,
        ConfigurationPortabilityClass portabilityClass,
        bool supportsPreview,
        bool supportsDiff,
        bool requiresExternalSetup,
        string? stableMappingIdentity = null,
        string? blockingReasonCode = null,
        string? warningReasonCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionKey);
        ConfigurationImportContractGuard.ValidateDigest(
            canonicalDigest,
            nameof(canonicalDigest));
        if (!Enum.IsDefined(portabilityClass))
            throw new ArgumentOutOfRangeException(nameof(portabilityClass));
        SectionKey = ConfigurationImportStableIdentity.Normalize(
            sectionKey,
            nameof(sectionKey));
        CanonicalDigest = canonicalDigest;
        PortabilityClass = portabilityClass;
        SupportsPreview = supportsPreview;
        SupportsDiff = supportsDiff;
        RequiresExternalSetup = requiresExternalSetup;
        StableMappingIdentity = string.IsNullOrWhiteSpace(stableMappingIdentity)
            ? null
            : ConfigurationImportStableIdentity.Normalize(
                stableMappingIdentity,
                nameof(stableMappingIdentity));
        BlockingReasonCode = string.IsNullOrWhiteSpace(blockingReasonCode)
            ? null
            : ConfigurationImportSafeCode.Normalize(
                blockingReasonCode,
                nameof(blockingReasonCode));
        WarningReasonCode = string.IsNullOrWhiteSpace(warningReasonCode)
            ? null
            : ConfigurationImportSafeCode.Normalize(
                warningReasonCode,
                nameof(warningReasonCode));
    }

    public string SectionKey { get; }
    public string CanonicalDigest { get; }
    public ConfigurationPortabilityClass PortabilityClass { get; }
    public bool SupportsPreview { get; }
    public bool SupportsDiff { get; }
    public bool RequiresExternalSetup { get; }
    public string? StableMappingIdentity { get; }
    public string? BlockingReasonCode { get; }
    public string? WarningReasonCode { get; }

}

public sealed record ConfigurationImportPreviewItem(
    string SectionKey,
    ConfigurationImportPreviewCategory Category,
    string ReasonCode,
    string? SourceMappingIdentity,
    string? TargetMappingIdentity);

public sealed record ConfigurationImportPreviewInput
{
    public ConfigurationImportPreviewInput(
        ConfigurationImportTarget target,
        string artifactDigest,
        string targetRevisionDigest,
        IEnumerable<ConfigurationImportSectionSnapshot> sourceSections,
        IEnumerable<ConfigurationImportSectionSnapshot> targetSections,
        IEnumerable<string> selectedSectionKeys,
        IEnumerable<KeyValuePair<string, string>> mappings,
        ConfigurationImportApplyMode applyMode,
        IEnumerable<string> requiredApprovalCodes,
        IEnumerable<string> grantedApprovalCodes,
        DateTime expiresAt)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(sourceSections);
        ArgumentNullException.ThrowIfNull(targetSections);
        ArgumentNullException.ThrowIfNull(selectedSectionKeys);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(requiredApprovalCodes);
        ArgumentNullException.ThrowIfNull(grantedApprovalCodes);
        ConfigurationImportContractGuard.ValidateDigest(
            artifactDigest,
            nameof(artifactDigest));
        ConfigurationImportContractGuard.ValidateDigest(
            targetRevisionDigest,
            nameof(targetRevisionDigest));
        if (!Enum.IsDefined(applyMode))
            throw new ArgumentOutOfRangeException(nameof(applyMode));
        ConfigurationImportContractGuard.RequireUtc(expiresAt, nameof(expiresAt));

        Target = target;
        ArtifactDigest = artifactDigest;
        TargetRevisionDigest = targetRevisionDigest;
        SourceSections = sourceSections
            .OrderBy(section => section.SectionKey, StringComparer.Ordinal)
            .ToImmutableArray();
        TargetSections = targetSections
            .OrderBy(section => section.SectionKey, StringComparer.Ordinal)
            .ToImmutableArray();
        SelectedSectionKeys = selectedSectionKeys
            .Select(value => ConfigurationImportStableIdentity.Normalize(
                value,
                nameof(selectedSectionKeys)))
            .ToImmutableHashSet(StringComparer.Ordinal);
        Mappings = mappings.ToImmutableDictionary(
            pair => ConfigurationImportStableIdentity.Normalize(
                pair.Key,
                nameof(mappings)),
            pair => ConfigurationImportStableIdentity.Normalize(
                pair.Value,
                nameof(mappings)),
            StringComparer.Ordinal);
        ApplyMode = applyMode;
        RequiredApprovalCodes = requiredApprovalCodes
            .Select(value => ConfigurationImportSafeCode.Normalize(
                value,
                nameof(requiredApprovalCodes)))
            .ToImmutableHashSet(StringComparer.Ordinal);
        GrantedApprovalCodes = grantedApprovalCodes
            .Select(value => ConfigurationImportSafeCode.Normalize(
                value,
                nameof(grantedApprovalCodes)))
            .ToImmutableHashSet(StringComparer.Ordinal);
        ExpiresAt = expiresAt;

        EnsureUniqueSections(SourceSections, nameof(sourceSections));
        EnsureUniqueSections(TargetSections, nameof(targetSections));
    }

    public ConfigurationImportTarget Target { get; }
    public string ArtifactDigest { get; }
    public string TargetRevisionDigest { get; }
    public ImmutableArray<ConfigurationImportSectionSnapshot> SourceSections { get; }
    public ImmutableArray<ConfigurationImportSectionSnapshot> TargetSections { get; }
    public ImmutableHashSet<string> SelectedSectionKeys { get; }
    public ImmutableDictionary<string, string> Mappings { get; }
    public ConfigurationImportApplyMode ApplyMode { get; }
    public ImmutableHashSet<string> RequiredApprovalCodes { get; }
    public ImmutableHashSet<string> GrantedApprovalCodes { get; }
    public DateTime ExpiresAt { get; }

    private static void EnsureUniqueSections(
        ImmutableArray<ConfigurationImportSectionSnapshot> sections,
        string parameterName)
    {
        if (sections.Select(section => section.SectionKey)
            .Distinct(StringComparer.Ordinal)
            .Count() != sections.Length)
        {
            throw new ArgumentException(
                "Configuration import section keys must be unique.",
                parameterName);
        }
    }
}

internal static class ConfigurationImportStableIdentity
{
    public static string Normalize(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length > 200
            || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-' and not '.'
                    and not ':' and not '/' and not '@'))
        {
            throw new ArgumentException(
                "Configuration import mapping identity must be a stable machine code.",
                parameterName);
        }

        return normalized;
    }
}

public sealed record ConfigurationImportPreview
{
    internal ConfigurationImportPreview(
        ConfigurationImportPreviewBinding binding,
        ImmutableArray<ConfigurationImportPreviewItem> items)
    {
        Binding = binding;
        Items = items;
    }

    public ConfigurationImportPreviewBinding Binding { get; }
    public ImmutableArray<ConfigurationImportPreviewItem> Items { get; }
    public bool IsApplyReady => Items.All(item =>
        item.Category is not ConfigurationImportPreviewCategory.Blocking
            and not ConfigurationImportPreviewCategory.ExternalSetupRequired);
}

public sealed class ConfigurationImportPreviewComposer
{
    public ConfigurationImportPreview Compose(
        ConfigurationImportPreviewInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var items = ImmutableArray.CreateBuilder<ConfigurationImportPreviewItem>();
        var targetBySection = input.TargetSections.ToDictionary(
            section => section.SectionKey,
            StringComparer.Ordinal);
        var mappedTargetIdentities = input.TargetSections
            .Where(section => section.StableMappingIdentity is not null)
            .ToDictionary(
                section => section.StableMappingIdentity!,
                StringComparer.Ordinal);

        foreach (ConfigurationImportSectionSnapshot source in input.SourceSections)
        {
            items.Add(Classify(
                source,
                targetBySection,
                mappedTargetIdentities,
                input));
        }

        foreach (ConfigurationImportSectionSnapshot target in input.TargetSections)
        {
            if (input.SourceSections.All(source =>
                    !string.Equals(
                        source.SectionKey,
                        target.SectionKey,
                        StringComparison.Ordinal)))
            {
                items.Add(new ConfigurationImportPreviewItem(
                    target.SectionKey,
                    ConfigurationImportPreviewCategory.Omitted,
                    "configuration_import_target_section_not_in_artifact",
                    SourceMappingIdentity: null,
                    target.StableMappingIdentity));
            }
        }

        foreach (string missingApproval in input.RequiredApprovalCodes.Except(
                     input.GrantedApprovalCodes,
                     StringComparer.Ordinal))
        {
            items.Add(new ConfigurationImportPreviewItem(
                $"approval.{missingApproval}",
                ConfigurationImportPreviewCategory.Blocking,
                "configuration_import_required_approval_missing",
                SourceMappingIdentity: null,
                TargetMappingIdentity: null));
        }

        var representedSections = items
            .Select(item => item.SectionKey)
            .ToHashSet(StringComparer.Ordinal);
        foreach (ConfigurationPortabilitySectionDescriptor descriptor in
                 ConfigurationPortabilityRegistry.Sections.Values
                     .Where(descriptor => IsRelevant(
                         descriptor.Scope,
                         input.Target.Scope))
                     .OrderBy(
                         descriptor => descriptor.Key,
                         StringComparer.Ordinal))
        {
            if (!representedSections.Add(descriptor.Key))
                continue;
            items.Add(new ConfigurationImportPreviewItem(
                descriptor.Key,
                ConfigurationImportPreviewCategory.Omitted,
                string.IsNullOrWhiteSpace(descriptor.OmissionReasonCode)
                    ? "configuration_import_section_absent"
                    : descriptor.OmissionReasonCode,
                SourceMappingIdentity: null,
                TargetMappingIdentity: null));
        }

        ConfigurationImportPreviewBinding binding = CreateBinding(input);
        return new ConfigurationImportPreview(
            binding,
            items
                .OrderBy(item => item.SectionKey, StringComparer.Ordinal)
                .ThenBy(item => item.Category)
                .ToImmutableArray());
    }

    private static ConfigurationImportPreviewItem Classify(
        ConfigurationImportSectionSnapshot source,
        IReadOnlyDictionary<string, ConfigurationImportSectionSnapshot>
            targetBySection,
        IReadOnlyDictionary<string, ConfigurationImportSectionSnapshot>
            mappedTargets,
        ConfigurationImportPreviewInput input)
    {
        if (!ConfigurationPortabilityRegistry.Sections.TryGetValue(
                source.SectionKey,
                out ConfigurationPortabilitySectionDescriptor? descriptor))
        {
            return Item(
                source,
                ConfigurationImportPreviewCategory.Blocking,
                "configuration_import_section_unknown");
        }

        if (IsExcluded(descriptor.PortabilityClass))
        {
            return Item(
                source,
                ConfigurationImportPreviewCategory.Omitted,
                "configuration_import_nonportable_section_omitted");
        }

        if (descriptor.PortabilityClass != source.PortabilityClass
            || descriptor.SupportsPreview != source.SupportsPreview
            || descriptor.SupportsDiff != source.SupportsDiff)
        {
            return Item(
                source,
                ConfigurationImportPreviewCategory.Blocking,
                "configuration_import_registry_mismatch");
        }

        if (descriptor.Dependencies.Any(dependency =>
                !input.SelectedSectionKeys.Contains(dependency)))
        {
            return Item(
                source,
                ConfigurationImportPreviewCategory.Blocking,
                "configuration_import_dependency_not_selected");
        }

        if (!descriptor.SupportsPreview || !descriptor.SupportsDiff)
        {
            return Item(
                source,
                ConfigurationImportPreviewCategory.Blocking,
                "configuration_import_preview_unsupported");
        }

        if (source.BlockingReasonCode is not null)
        {
            return Item(
                source,
                ConfigurationImportPreviewCategory.Blocking,
                source.BlockingReasonCode);
        }

        if (!input.SelectedSectionKeys.Contains(source.SectionKey))
        {
            return Item(
                source,
                ConfigurationImportPreviewCategory.Skipped,
                "configuration_import_section_not_selected");
        }

        if (source.RequiresExternalSetup)
        {
            return Item(
                source,
                ConfigurationImportPreviewCategory.ExternalSetupRequired,
                "configuration_import_external_setup_required");
        }

        if (source.WarningReasonCode is not null)
        {
            return Item(
                source,
                ConfigurationImportPreviewCategory.Warning,
                source.WarningReasonCode);
        }

        if (descriptor.PortabilityClass
                == ConfigurationPortabilityClass.PortableWithMapping)
        {
            if (source.StableMappingIdentity is null)
            {
                return Item(
                    source,
                    ConfigurationImportPreviewCategory.Blocking,
                    "configuration_import_mapping_identity_missing");
            }

            if (!input.Mappings.TryGetValue(
                    source.StableMappingIdentity,
                    out string? mappedIdentity))
            {
                return Item(
                    source,
                    ConfigurationImportPreviewCategory.Blocking,
                    "configuration_import_mapping_required");
            }

            if (!mappedTargets.ContainsKey(mappedIdentity))
            {
                return new ConfigurationImportPreviewItem(
                    source.SectionKey,
                    ConfigurationImportPreviewCategory.Blocking,
                    "configuration_import_mapping_target_missing",
                    source.StableMappingIdentity,
                    mappedIdentity);
            }

            return new ConfigurationImportPreviewItem(
                source.SectionKey,
                ConfigurationImportPreviewCategory.Mapped,
                "configuration_import_stable_identity_mapped",
                source.StableMappingIdentity,
                mappedIdentity);
        }

        if (!targetBySection.TryGetValue(
                source.SectionKey,
                out ConfigurationImportSectionSnapshot? target))
        {
            return Item(
                source,
                ConfigurationImportPreviewCategory.Changed,
                "configuration_import_section_added");
        }

        return Item(
            source,
            string.Equals(
                source.CanonicalDigest,
                target.CanonicalDigest,
                StringComparison.Ordinal)
                ? ConfigurationImportPreviewCategory.Unchanged
                : ConfigurationImportPreviewCategory.Changed,
            string.Equals(
                source.CanonicalDigest,
                target.CanonicalDigest,
                StringComparison.Ordinal)
                ? "configuration_import_section_unchanged"
                : "configuration_import_section_changed");
    }

    private static ConfigurationImportPreviewBinding CreateBinding(
        ConfigurationImportPreviewInput input) =>
        new(
            input.Target,
            input.ArtifactDigest,
            input.TargetRevisionDigest,
            ConfigurationImportDigest.Compute(input.SelectedSectionKeys),
            ConfigurationImportDigest.Compute(input.Mappings.Select(pair =>
                $"{pair.Key}\u001f{pair.Value}")),
            input.ApplyMode,
            ConfigurationImportDigest.Compute(
                input.RequiredApprovalCodes.Select(code => $"required:{code}")
                    .Concat(input.GrantedApprovalCodes.Select(code =>
                        $"granted:{code}"))),
            input.ExpiresAt);

    private static ConfigurationImportPreviewItem Item(
        ConfigurationImportSectionSnapshot source,
        ConfigurationImportPreviewCategory category,
        string reasonCode) =>
        new(
            source.SectionKey,
            category,
            reasonCode,
            source.StableMappingIdentity,
            TargetMappingIdentity: null);

    private static bool IsExcluded(ConfigurationPortabilityClass portabilityClass) =>
        portabilityClass is
            ConfigurationPortabilityClass.Secret
            or ConfigurationPortabilityClass.PersonallyIdentifiableInformation
            or ConfigurationPortabilityClass.ApplicationData
            or ConfigurationPortabilityClass.OperationalState
            or ConfigurationPortabilityClass.ProviderBinding
            or ConfigurationPortabilityClass.DeploymentTopology;

    private static bool IsRelevant(
        ConfigurationPortabilityScope scope,
        ConfigurationImportScope targetScope) =>
        targetScope == ConfigurationImportScope.Instance
        || scope is ConfigurationPortabilityScope.Tenant
            or ConfigurationPortabilityScope.Shared
            or ConfigurationPortabilityScope.Excluded;
}

public static class ConfigurationImportDigest
{
    public static string Compute(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        string canonical = string.Join(
            '\u001e',
            values.Order(StringComparer.Ordinal));
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    public static string ComputeBytes(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
