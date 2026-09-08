namespace Explore.Application.Features.ConfigurationManifest.Importing;

using System.Text.Json;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public static class TenantConfigurationPackageSerializer
{
    public static TenantConfigurationPackageV1Alpha2 Create(
        ConfigurationManifestV1Alpha2 manifest,
        ConfigurationManifestTenantV1Alpha2 tenant)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(tenant);
        return new TenantConfigurationPackageV1Alpha2
        {
            Schema = TenantConfigurationPackageContractMetadata.SchemaId,
            ApiVersion = TenantConfigurationPackageContractMetadata.ApiVersion,
            Kind = TenantConfigurationPackageContractMetadata.Kind,
            Metadata = new TenantConfigurationPackageMetadataV1Alpha2
            {
                Name = tenant.Metadata.Name,
                Source = new TenantConfigurationPackageSourceV1Alpha2
                {
                    TenantName = tenant.Metadata.Name,
                    InstanceName = manifest.Metadata.Name
                },
                Export = new ConfigurationManifestExportMetadataV1Alpha2
                {
                    View = manifest.Metadata.Export?.View
                        ?? ConfigurationManifestExportMetadataValues.OverridesView,
                    EffectiveValuesFlattened = manifest.Metadata.Export?
                        .EffectiveValuesFlattened ?? false,
                    SensitiveValuesOmitted = true,
                    AuthorityScope = ConfigurationManifestExportMetadataValues
                        .TenantAuthorityScope,
                    SovereignValuesOmitted = true,
                    SovereignLockedFields = []
                }
            },
            Spec = new TenantConfigurationPackageSpecV1Alpha2
            {
                DisplayName = tenant.Spec.DisplayName,
                Settings = tenant.Spec.Settings,
                Documents = tenant.Spec.Documents,
                LegalDocuments = tenant.Spec.LegalDocuments
            }
        };
    }

    public static ReadOnlyMemory<byte> Serialize(
        TenantConfigurationPackageV1Alpha2 package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return JsonSerializer.SerializeToUtf8Bytes(
            package,
            ConfigurationPortabilityJsonContext.Default
                .TenantConfigurationPackageV1Alpha2);
    }
}
