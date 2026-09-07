namespace Explore.Application.Features.TenantSettingsDocuments.Requests.Commands;

using Explore.Application.Authorization;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Responses;
using MediatR;

[AuthorizeResource(ResourceKinds.TenantSetting, AuthorizationActions.Update)]
public sealed record PatchTenantDirectoryOperatorIdentityDocumentCommand
    : IRequest<BaseCommandResponse<TenantDirectoryOperatorIdentityDocumentDto>>, ISecureRequest
{
    public required Guid TenantId { get; init; }
    public required PatchTenantDirectoryOperatorIdentityDocumentDto Patch { get; init; }

    string? ISecureRequest.ResourceId => TenantId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantSettingAuthorizationFacts(
            TenantId,
            Explore.Domain.Settings.Documents.SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity);
}
