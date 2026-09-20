using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.PlatformMonetization;

namespace Explore.Application.Features.PlatformMonetization.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetPlatformMonetizationSettingsQuery : IQuery<PlatformMonetizationSettingsDto>, ISecureRequest
{
    public const string SettingKey = "platform-monetization";

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
