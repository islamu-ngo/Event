namespace Event.Application.UnitTests.Features.ConfigurationManifest;

using System.Text.Json;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

internal static class ConfigurationManifestTestData
{
    public static ConfigurationManifestV1Alpha2 Valid(
        string tenantSlug = "default",
        IReadOnlyDictionary<string, JsonElement>? settings = null,
        IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2>? documents = null,
        IReadOnlyDictionary<string, JsonElement>? instanceSettings = null,
        IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2>? instanceDocuments = null) =>
        new()
        {
            Schema = ConfigurationManifestContractMetadata.SchemaId,
            ApiVersion = ConfigurationManifestContractMetadata.ApiVersion,
            Kind = ConfigurationManifestContractMetadata.Kind,
            Metadata = new ConfigurationManifestMetadataV1Alpha2
            {
                Name = "primary-deployment"
            },
            Spec = new ConfigurationManifestSpecV1Alpha2
            {
                Instance = new ConfigurationManifestInstanceV1Alpha2
                {
                    Settings = instanceSettings
                        ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal),
                    Documents = instanceDocuments
                        ?? new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
                            StringComparer.Ordinal)
                },
                Tenants =
                [
                    new ConfigurationManifestTenantV1Alpha2
                    {
                        Metadata = new ConfigurationManifestTenantMetadataV1Alpha2
                        {
                            Name = tenantSlug
                        },
                        Spec = new ConfigurationManifestTenantSpecV1Alpha2
                        {
                            DisplayName = "Primary Community",
                            Settings = settings ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal),
                            Documents = documents
                                ?? new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
                                    StringComparer.Ordinal)
                        }
                    }
                ]
            }
        };

    public static JsonElement Json(string value) =>
        JsonDocument.Parse(value).RootElement.Clone();
}
