namespace Explore.Application.Features.ConfigurationManifest.Handlers.Queries;

using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using FluentValidation;

public sealed class ExportConfigurationManifestQueryHandler(
    IConfigurationManifestExportExecutor executor)
    : IQueryHandler<ExportConfigurationManifestQuery, ConfigurationManifestExportResult>
{
    public async Task<ConfigurationManifestExportResult> QueryAsync(
        ExportConfigurationManifestQuery request,
        CancellationToken cancellationToken)
    {
        await new ExportConfigurationManifestQueryValidator()
            .ValidateAndThrowAsync(request, cancellationToken);
        return await executor.ExportAsync(request.View, request.Scope, cancellationToken);
    }
}
