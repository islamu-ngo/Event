namespace Explore.Application.Features.TenantSettingsDocuments.Requests.Queries;

using Explore.Application.Authorization;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Domain.Settings.Documents;
using MediatR;

[AuthorizeResource(ResourceKinds.TenantSetting, AuthorizationActions.TenantSettings.View)]
public sealed record GetTenantDirectoryOperatorIdentityDocumentQuery(Guid TenantId)
    : IRequest<TenantDirectoryOperatorIdentityDocumentDto?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => TenantId == Guid.Empty
        ? null
        : $"{TenantId}:{SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity}";

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantSettingAuthorizationFacts(
            TenantId,
            SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity);
}
