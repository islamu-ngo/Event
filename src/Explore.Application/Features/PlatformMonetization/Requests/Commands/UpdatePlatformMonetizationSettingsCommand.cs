using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.PlatformMonetization;
using Explore.Application.Features.PlatformMonetization.Requests.Queries;
using Explore.Application.Responses;

namespace Explore.Application.Features.PlatformMonetization.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record UpdatePlatformMonetizationSettingsCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public const string SettingKey = GetPlatformMonetizationSettingsQuery.SettingKey;

    public UpdatePlatformMonetizationSettingsDto Settings { get; init; } = new();

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
