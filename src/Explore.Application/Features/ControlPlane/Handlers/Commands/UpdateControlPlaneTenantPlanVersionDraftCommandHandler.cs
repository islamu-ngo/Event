using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ControlPlane.Plans;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Handlers.Commands;

public sealed class UpdateControlPlaneTenantPlanVersionDraftCommandHandler(ITenantPlanRepository tenantPlanRepository)
    : IRequestHandler<UpdateControlPlaneTenantPlanVersionDraftCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdateControlPlaneTenantPlanVersionDraftCommand request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> shapeErrors = ValidateShape(request.Update);
        if (shapeErrors.Count > 0)
        {
            return Failure("Tenant plan draft update is invalid.", shapeErrors);
        }

        TenantPlanVersion? version = await tenantPlanRepository.GetVersionForUpdateAsync(
            request.VersionId,
            cancellationToken);
        if (version is null)
        {
            return Failure("Tenant plan version was not found.", ["tenant_plan_version_not_found"]);
        }

        if (version.TenantPlanStatusId != (int)TenantPlanStatusEnum.Draft)
        {
            return Failure("Only draft tenant plan versions can be updated.", ["tenant_plan_version_not_draft"]);
        }

        TenantPlanDraft effectiveDraft = BuildEffectiveDraft(version, request.Update);
        TenantPlanValidationResult validation = TenantPlanDraftValidator.Validate(effectiveDraft);
        if (!validation.IsValid)
        {
            return Failure("Tenant plan draft is invalid.", validation.Errors.Select(error => error.Code));
        }

        ApplyUpdates(version, request.Update);
        await tenantPlanRepository.UpdateVersionAsync(version, cancellationToken);

        return BaseCommandResponse.Success(version.Id, "Tenant plan draft version updated.");
    }

    private static BaseCommandResponse<Guid> Failure(string message, IEnumerable<string> errors) =>
        BaseCommandResponse.Validation<Guid>(errors, message);

    private static IReadOnlyList<string> ValidateShape(PatchControlPlaneTenantPlanVersionDraftDto update)
    {
        var errors = new List<string>();
        if (update.Pricing is null
            && update.IsActiveForProvisioning is null
            && update.SettingOverrides is null
            && update.QuotaLimits is null)
        {
            errors.Add("tenant_plan_update_empty");
        }

        if (update.Pricing is { } pricing
            && (pricing.Amount is null
                || string.IsNullOrWhiteSpace(pricing.CurrencyCode)
                || string.IsNullOrWhiteSpace(pricing.BillingPeriod)))
        {
            errors.Add("tenant_plan_pricing_incomplete");
        }

        if (update.IsActiveForProvisioning is { Value: null })
            errors.Add("tenant_plan_provisioning_incomplete");

        if (update.SettingOverrides is { Values: null })
            errors.Add("tenant_plan_settings_incomplete");

        if (update.QuotaLimits is { Values: null })
            errors.Add("tenant_plan_quotas_incomplete");

        return errors;
    }

    private static TenantPlanDraft BuildEffectiveDraft(
        TenantPlanVersion version,
        PatchControlPlaneTenantPlanVersionDraftDto update) =>
        new(
            version.TenantPlan.Key,
            version.TenantPlan.DisplayName,
            update.Pricing is null
                ? new TenantPlanPricing(version.PriceAmount, version.CurrencyCode, version.BillingPeriod)
                : new TenantPlanPricing(
                    update.Pricing.Amount!.Value,
                    update.Pricing.CurrencyCode!,
                    update.Pricing.BillingPeriod!),
            update.IsActiveForProvisioning?.Value ?? version.IsActiveForProvisioning,
            update.SettingOverrides?.Values
                ?? version.Settings.Select(setting => new TenantPlanSettingOverride(
                    setting.SettingKey,
                    setting.JsonValue,
                    setting.IsLocked)).ToList(),
            update.QuotaLimits?.Values
                ?? version.Quotas.Select(quota => new TenantPlanQuotaLimit(quota.QuotaKey, quota.Limit)).ToList());

    private static void ApplyUpdates(
        TenantPlanVersion version,
        PatchControlPlaneTenantPlanVersionDraftDto update)
    {
        if (update.Pricing is not null)
        {
            ControlPlaneTenantPlanDraftMapper.ApplyPricing(
                version,
                new TenantPlanPricing(
                    update.Pricing.Amount!.Value,
                    update.Pricing.CurrencyCode!,
                    update.Pricing.BillingPeriod!));
        }

        if (update.IsActiveForProvisioning?.Value is bool isActiveForProvisioning)
            version.IsActiveForProvisioning = isActiveForProvisioning;

        if (update.SettingOverrides?.Values is { } settings)
            ControlPlaneTenantPlanDraftMapper.ReplaceSettings(version, settings);

        if (update.QuotaLimits?.Values is { } quotas)
            ControlPlaneTenantPlanDraftMapper.ReplaceQuotas(version, quotas);
    }
}
