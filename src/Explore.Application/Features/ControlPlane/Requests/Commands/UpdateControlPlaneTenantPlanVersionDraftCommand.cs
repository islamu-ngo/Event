using Explore.Application.Authorization;
using Explore.Application.Features.ControlPlane.Plans;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record UpdateControlPlaneTenantPlanVersionDraftCommand
    : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public UpdateControlPlaneTenantPlanVersionDraftCommand(
        Guid versionId,
        PatchControlPlaneTenantPlanVersionDraftDto update)
    {
        VersionId = versionId;
        Update = update;
    }

    public const string SettingKey = "control-plane.tenant-plans";

    public Guid VersionId { get; }
    public PatchControlPlaneTenantPlanVersionDraftDto Update { get; }

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}

public sealed record PatchControlPlaneTenantPlanVersionDraftDto
{
    public PatchTenantPlanPricingDto? Pricing { get; init; }
    public PatchTenantPlanProvisioningDto? IsActiveForProvisioning { get; init; }
    public PatchTenantPlanSettingOverridesDto? SettingOverrides { get; init; }
    public PatchTenantPlanQuotaLimitsDto? QuotaLimits { get; init; }
}

public sealed record PatchTenantPlanPricingDto
{
    public decimal? Amount { get; init; }
    public string? CurrencyCode { get; init; }
    public string? BillingPeriod { get; init; }
}

public sealed record PatchTenantPlanProvisioningDto
{
    public bool? Value { get; init; }
}

public sealed record PatchTenantPlanSettingOverridesDto
{
    public IReadOnlyList<TenantPlanSettingOverride>? Values { get; init; }
}

public sealed record PatchTenantPlanQuotaLimitsDto
{
    public IReadOnlyList<TenantPlanQuotaLimit>? Values { get; init; }
}
