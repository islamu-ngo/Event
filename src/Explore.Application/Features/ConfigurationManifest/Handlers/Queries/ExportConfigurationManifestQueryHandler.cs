namespace Explore.Application.Features.ConfigurationManifest.Handlers.Queries;

using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;

public sealed class ExportConfigurationManifestQueryHandler(
    ConfigurationManifestCurrentStateReader currentState)
    : IQueryHandler<ExportConfigurationManifestQuery, ConfigurationManifestExportResult>
{
    public Task<ConfigurationManifestExportResult> QueryAsync(
        ExportConfigurationManifestQuery request,
        CancellationToken cancellationToken) =>
        currentState.ReadAsync(request.View, cancellationToken);
}
