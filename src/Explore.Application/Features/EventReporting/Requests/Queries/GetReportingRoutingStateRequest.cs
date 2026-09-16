using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventReporting;

namespace Explore.Application.Features.EventReporting.Requests.Queries;

[AuthorizeResource(ResourceKinds.TenantSetting, AuthorizationActions.TenantSettings.View)]
public sealed record GetReportingRoutingStateRequest(Guid TenantId) : IQuery<ReportingRoutingStateDto>, ISecureRequest
{
    private const string SettingKey = "moderation-reporting";

    public string? ResourceId => $"{TenantId}:{SettingKey}";

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantSettingAuthorizationFacts(TenantId);
}
