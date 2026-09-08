namespace Explore.Application.Features.TenantSettingsDocuments.Handlers.Queries;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Queries;
using Explore.Application.Settings;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using MediatR;

public sealed class GetTenantDirectoryOperatorIdentityDocumentQueryHandler(
    ITenantContext tenantContext,
    ITypedSettingsDocumentResolver settingsDocumentResolver)
    : IRequestHandler<
        GetTenantDirectoryOperatorIdentityDocumentQuery,
        TenantDirectoryOperatorIdentityDocumentDto?>
{
    public async Task<TenantDirectoryOperatorIdentityDocumentDto?> Handle(
        GetTenantDirectoryOperatorIdentityDocumentQuery request,
        CancellationToken cancellationToken)
    {
        _ = request;
        Guid tenantId = tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return null;
        }

        const string documentKey = SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity;
        var resolved = await settingsDocumentResolver
            .ResolveTenantDocumentAsync<TenantDirectoryOperatorIdentitySettings>(
                new SettingsResolutionContext(
                    tenantId,
                    RequestedDocuments: [documentKey]),
                documentKey,
                cancellationToken);

        if (resolved is null
            || resolved.Source != SettingsDocumentSource.Tenant
            || resolved.SourceScopeId != tenantId
            || !string.Equals(resolved.DocumentKey, documentKey, StringComparison.Ordinal)
            || resolved.SchemaVersion != TenantDirectoryOperatorIdentityDocumentDefaults.SchemaVersion)
        {
            return null;
        }

        return TenantDirectoryOperatorIdentityDocumentMapper.Map(resolved);
    }
}
