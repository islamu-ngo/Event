namespace Explore.Application.Features.ConfigurationManifest.Requests.Queries;

using Explore.Application.Authorization;
using MediatR;

[AuthorizeResource(
    ResourceKinds.TenantSetting,
    AuthorizationActions.TenantSettings.View)]
public sealed record ExportTenantConfigurationPackageQuery(
    Guid TenantId,
    ConfigurationManifestExportView View = ConfigurationManifestExportView.Overrides)
    : IRequest<TenantConfigurationPackageExportResult>, ISecureRequest
{
    public const string ResourceKey = "tenant.configuration-package.export";
    string? ISecureRequest.ResourceId => ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantSettingAuthorizationFacts(TenantId, ResourceKey);
}

public sealed record TenantConfigurationPackageExportResult(
    ConfigurationManifestExportView View,
    string FileName,
    ReadOnlyMemory<byte> Utf8Json,
    string Sha256Digest)
{
    public override string ToString() => nameof(TenantConfigurationPackageExportResult);
}
