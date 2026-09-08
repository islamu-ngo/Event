using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ControlPlane.Plans;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Handlers.Commands;

public sealed class CloneControlPlaneTenantPlanCommandHandler(ITenantPlanRepository tenantPlanRepository)
    : IRequestHandler<CloneControlPlaneTenantPlanCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        CloneControlPlaneTenantPlanCommand request,
        CancellationToken cancellationToken)
    {
        TenantPlanVersion? source = await tenantPlanRepository.GetVersionAsync(request.SourceVersionId, cancellationToken);
        if (source is null)
        {
            return Failure("Tenant plan version was not found.", ["tenant_plan_version_not_found"]);
        }

        if (source.TenantPlanStatusId != (int)TenantPlanStatusEnum.Published)
        {
            return Failure("Only published tenant plan versions can be cloned.", ["tenant_plan_version_not_published"]);
        }

        TenantPlan? existing = await tenantPlanRepository.GetByKeyAsync(request.Key, cancellationToken);
        if (existing is not null)
        {
            return Failure("A tenant plan with this key already exists.", ["tenant_plan_key_exists"]);
        }

        var draft = new TenantPlanDraft(
            request.Key,
            request.Name,
            new TenantPlanPricing(source.PriceAmount, source.CurrencyCode, source.BillingPeriod),
            source.IsActiveForProvisioning,
            source.Settings
                .OrderBy(setting => setting.SettingKey)
                .Select(setting => new TenantPlanSettingOverride(setting.SettingKey, setting.JsonValue, setting.IsLocked))
                .ToArray(),
            source.Quotas
                .OrderBy(quota => quota.QuotaKey)
                .Select(quota => new TenantPlanQuotaLimit(quota.QuotaKey, quota.Limit))
                .ToArray());

        TenantPlanValidationResult validation = TenantPlanDraftValidator.Validate(draft);
        if (!validation.IsValid)
        {
            return Failure("Tenant plan draft is invalid.", validation.Errors.Select(error => error.Code));
        }

        TenantPlan clone = ControlPlaneTenantPlanDraftMapper.ToPlan(draft);
        TenantPlanVersion clonedVersion = clone.Versions.Single();
        clonedVersion.TenantPlanStatusId = (int)TenantPlanStatusEnum.Draft;
        await tenantPlanRepository.Create(clone);

        return BaseCommandResponse.Success(clone.Id, "Tenant plan cloned as draft.");
    }

    private static BaseCommandResponse<Guid> Failure(string message, IEnumerable<string> errors) =>
        BaseCommandResponse.Validation<Guid>(errors, message);
}
