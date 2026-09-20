using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Responses;
using Explore.Domain.Constants;

namespace Explore.Application.Features.EventReporting.Requests.Commands;

[AuthorizeResource(ResourceKinds.TenantSetting, AuthorizationActions.TenantSettings.Update)]
public sealed record UpdateTenantReportingIntakePolicyCommand(
    Guid TenantId,
    Guid ActorUserId,
    UpdateTenantReportingIntakePolicyDto Policy)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => GovernanceSettingKeys.EventReporting.IntakeEnabled;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => new TenantSettingAuthorizationFacts(
        TenantId,
        GovernanceSettingKeys.EventReporting.IntakeEnabled);
}
