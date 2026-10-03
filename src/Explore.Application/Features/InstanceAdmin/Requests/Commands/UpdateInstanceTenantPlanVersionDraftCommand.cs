using Explore.Application.Authorization;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record UpdateInstanceTenantPlanVersionDraftCommand
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public UpdateInstanceTenantPlanVersionDraftCommand(
        Guid versionId,
        PatchInstanceTenantPlanVersionDraftDto update)
    {
        VersionId = versionId;
        Update = update;
    }

    public const string SettingKey = "control-plane.tenant-plans";

    public Guid VersionId { get; }
    public PatchInstanceTenantPlanVersionDraftDto Update { get; }

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}

public sealed record PatchInstanceTenantPlanVersionDraftDto
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
