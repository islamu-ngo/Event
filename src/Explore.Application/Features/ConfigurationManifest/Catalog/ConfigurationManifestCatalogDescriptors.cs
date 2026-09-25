namespace Explore.Application.Features.ConfigurationManifest.Catalog;

using System.Collections.Immutable;
using Explore.Domain.Settings;

public enum ConfigurationManifestScope
{
    Instance,
    Tenant
}

public sealed record ConfigurationManifestSettingCatalogEntry(
    ConfigurationManifestScope Scope,
    SettingDefinition Definition,
    int? MaximumStringLength = null,
    ConfigurationManifestStringArrayDescriptor? StringArray = null);

/// <summary>Declares JSON string-array items; null allowed values leave semantic constraints to the owning policy.</summary>
public sealed record ConfigurationManifestStringArrayDescriptor
{
    public ConfigurationManifestStringArrayDescriptor(IEnumerable<string>? allowedValues = null)
    {
        AllowedValues = allowedValues?.ToImmutableArray();
    }

    public ImmutableArray<string>? AllowedValues { get; }
}

public sealed record ConfigurationManifestDocumentCatalogEntry(
    ConfigurationManifestScope Scope,
    string DocumentKey,
    int SchemaVersion,
    string? DefaultsVersion,
    Type PayloadType,
    ConfigurationManifestDocumentStorage Storage);

public enum ConfigurationManifestDocumentStorage
{
    TenantSettingsDocument,
    PaidEventPolicy
}
