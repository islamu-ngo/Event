namespace Explore.Application.Features.ConfigurationManifest.Importing;

using Explore.Application.DTOs.PaidEventPolicies;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public static class TenantConfigurationPackageSerializer
{
    public static TenantConfigurationPackageV1Alpha2 Create(
        ConfigurationManifestV1Alpha2 manifest,
        ConfigurationManifestTenantV1Alpha2 tenant)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return Create(manifest.Metadata, tenant);
    }

    public static TenantConfigurationPackageV1Alpha2 Create(
        ConfigurationManifestMetadataV1Alpha2 metadata,
        ConfigurationManifestTenantV1Alpha2 tenant)
    {
        ArgumentNullException.ThrowIfNull(metadata);
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
                    InstanceName = metadata.Name
                },
                Export = new ConfigurationManifestExportMetadataV1Alpha2
                {
                    View = metadata.Export?.View
                        ?? ConfigurationManifestExportMetadataValues.OverridesView,
                    EffectiveValuesFlattened = metadata.Export?
                        .EffectiveValuesFlattened ?? false,
                    SensitiveValuesOmitted = true,
                    AuthorityScope = ConfigurationManifestExportMetadataValues
                        .TenantAuthorityScope,
                    SovereignValuesOmitted = true,
                    SovereignLockedFields =
                        PaidEventPolicyAuthorityMetadata.SovereignLockedFields
                }
            },
            Spec = new TenantConfigurationPackageSpecV1Alpha2
            {
                DisplayName = tenant.Spec.DisplayName,
                Settings = tenant.Spec.Settings,
                Documents = tenant.Spec.Documents,
                LegalDocuments = tenant.Spec.LegalDocuments,
                Moderation = tenant.Spec.Moderation
            }
        };
    }

    public static ReadOnlyMemory<byte> Serialize(
        TenantConfigurationPackageV1Alpha2 package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(package);
    }
}
