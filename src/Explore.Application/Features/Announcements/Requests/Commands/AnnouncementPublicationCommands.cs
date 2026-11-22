using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Domain.Constants;
using Explore.Domain.Settings;

namespace Explore.Application.Features.Announcements.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record UpdateInstanceAnnouncementCommand(Guid ActorUserId, AnnouncementUpdate Update)
    : ICommand<AnnouncementPublicationResult>, ISecureRequest
{
    string? ISecureRequest.ResourceId => GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => InstanceScopedAuthorizationFacts.Instance;
}

[AuthorizeResource(ResourceKinds.TenantSetting, AuthorizationActions.TenantSettings.Update)]
public sealed record UpdateTenantAnnouncementCommand(Guid TenantId, Guid ActorUserId, AnnouncementUpdate Update)
    : ICommand<AnnouncementPublicationResult>, ISecureRequest
{
    string? ISecureRequest.ResourceId => GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantSettingAuthorizationFacts(TenantId, GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication);
}

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record UpdateManagedTenantAnnouncementCommand(Guid TenantId, Guid ActorUserId, AnnouncementUpdate Update)
    : ICommand<AnnouncementPublicationResult>, ISecureRequest
{
    string? ISecureRequest.ResourceId => "control-plane.tenant-effective-configuration";
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => InstanceScopedAuthorizationFacts.Instance;
}
