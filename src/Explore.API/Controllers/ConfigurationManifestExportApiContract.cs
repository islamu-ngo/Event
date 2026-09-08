namespace Explore.API.Controllers;

using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;

public static class ConfigurationManifestExportApiContract
{
    public const string MediaType = ConfigurationManifestContractMetadata.MediaType;
    public const string OverridesFileName = "configuration-manifest-overrides.json";
    public const string PortableFileName = "configuration-manifest-portable.json";
    public const string TooLargeFailureCode =
        ConfigurationManifestExportContract.TooLargeFailureCode;
}
