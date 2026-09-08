using Explore.Application.Authorization;
using Explore.Application.DTOs.PlatformMonetization;
using MediatR;

namespace Explore.Application.Features.PlatformMonetization.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetPlatformMonetizationSettingsQuery : IRequest<PlatformMonetizationSettingsDto>, ISecureRequest
{
    public const string SettingKey = "platform-monetization";

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
