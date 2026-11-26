using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Domain.Constants;
using Explore.Domain.Settings;

namespace Explore.Application.Features.Announcements;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetInstanceAnnouncementSourceQuery : IQuery<AnnouncementSourceSnapshot>, ISecureRequest
{
    string? ISecureRequest.ResourceId => GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => InstanceScopedAuthorizationFacts.Instance;
}

[AuthorizeResource(ResourceKinds.TenantSetting, AuthorizationActions.TenantSettings.View)]
public sealed record GetTenantAnnouncementSourceQuery(Guid TenantId) : IQuery<AnnouncementSourceSnapshot>, ISecureRequest
{
    string? ISecureRequest.ResourceId => GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantSettingAuthorizationFacts(TenantId, GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication);
}

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetManagedTenantAnnouncementSourceQuery(Guid TenantId) : IQuery<AnnouncementSourceSnapshot>, ISecureRequest
{
    string? ISecureRequest.ResourceId => "control-plane.tenant-effective-configuration";
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => InstanceScopedAuthorizationFacts.Instance;
}
