namespace Explore.Application.Features.ConfigurationManifest.Handlers.Queries;

using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;

public sealed class ExportTenantConfigurationPackageQueryHandler(
    ConfigurationManifestCurrentStateReader currentState) : IQueryHandler<
        ExportTenantConfigurationPackageQuery,
        TenantConfigurationPackageExportResult>
{
    public Task<TenantConfigurationPackageExportResult> QueryAsync(
        ExportTenantConfigurationPackageQuery request,
        CancellationToken cancellationToken) =>
        currentState.ReadTenantAsync(request.TenantId, request.View, cancellationToken);
}
