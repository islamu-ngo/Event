namespace Explore.Application.Features.EventReporting.Requests.Commands;

using Explore.Application.Authorization;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Responses;
using MediatR;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record UpdateReportingProviderLocksCommand(
    Guid UserId,
    UpdateReportingProviderLocksDto Locks)
    : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    private const string SettingKey = "moderation-reporting-locks";

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
