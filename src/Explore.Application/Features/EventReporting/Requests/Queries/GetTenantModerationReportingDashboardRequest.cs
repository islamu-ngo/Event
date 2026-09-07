using Explore.Application.Authorization;
using Explore.Application.DTOs.EventReporting;
using MediatR;

namespace Explore.Application.Features.EventReporting.Requests.Queries;

[AuthorizeResource(ResourceKinds.TenantSetting, AuthorizationActions.TenantSettings.View)]
public sealed record GetTenantModerationReportingDashboardRequest(Guid TenantId)
    : IRequest<TenantModerationReportingDashboardDto>, ISecureRequest
{
    private const string SettingKey = "moderation-reporting";

    string? ISecureRequest.ResourceId => $"{TenantId}:{SettingKey}";

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantSettingAuthorizationFacts(TenantId);
}
