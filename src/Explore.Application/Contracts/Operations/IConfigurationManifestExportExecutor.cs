using Explore.Application.Features.ConfigurationManifest.Requests.Queries;

namespace Explore.Application.Contracts.Operations;

/// <summary>Buffers a public manifest under the authenticated instance administrator's export authority.</summary>
public interface IConfigurationManifestExportExecutor
{
    Task<ConfigurationManifestExportResult> ExportAsync(
        ConfigurationManifestExportView view, ConfigurationManifestExportScope scope, CancellationToken token);
}
