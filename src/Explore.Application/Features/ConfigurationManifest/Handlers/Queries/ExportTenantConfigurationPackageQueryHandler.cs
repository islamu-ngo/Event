namespace Explore.Application.Features.ConfigurationManifest.Handlers.Queries;

using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Domain;

public sealed class ExportTenantConfigurationPackageQueryHandler(
    ConfigurationManifestCurrentStateReader currentState,
    ConfigurationImportArtifactParser parser,
    ITenantRepository tenants) : IQueryHandler<
        ExportTenantConfigurationPackageQuery,
        TenantConfigurationPackageExportResult>
{
    public async Task<TenantConfigurationPackageExportResult> QueryAsync(
        ExportTenantConfigurationPackageQuery request,
        CancellationToken cancellationToken)
    {
        Tenant tenant = await tenants.GetByIdAsNoTrackingAsync(
                request.TenantId,
                cancellationToken)
            ?? throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ArtifactMissing);
        ConfigurationManifestExportResult manifest = await currentState.ReadAsync(
            request.View,
            cancellationToken);
        ConfigurationImportParsedArtifact parsed = parser.Parse(manifest.Utf8Json);
        var selected = parsed.Manifest.Spec.Tenants
            .SingleOrDefault(candidate => string.Equals(
                candidate.Metadata.Name,
                tenant.Slug,
                StringComparison.Ordinal))
            ?? throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ArtifactMissing);
        ReadOnlyMemory<byte> bytes = TenantConfigurationPackageSerializer.Serialize(
            TenantConfigurationPackageSerializer.Create(
                parsed.Manifest,
                selected));
        return new TenantConfigurationPackageExportResult(
            request.View,
            $"tenant-configuration-package-{tenant.Slug}.json",
            bytes,
            ConfigurationImportDigest.ComputeBytes(bytes.Span));
    }
}
