using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventReporting;
using Explore.Domain.Constants;

namespace Explore.Application.Features.EventReporting.Requests.Queries;

[AuthorizeResource(ResourceKinds.TenantSetting, AuthorizationActions.TenantSettings.View)]
public sealed record GetTenantReportingIntakePolicyQuery(Guid TenantId)
    : IQuery<TenantReportingIntakePolicyDto>, ISecureRequest
{
    string? ISecureRequest.ResourceId => GovernanceSettingKeys.EventReporting.IntakeEnabled;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => new TenantSettingAuthorizationFacts(
        TenantId,
        GovernanceSettingKeys.EventReporting.IntakeEnabled);
}
