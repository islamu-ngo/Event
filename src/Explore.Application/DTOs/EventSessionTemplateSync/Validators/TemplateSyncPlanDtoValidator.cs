using Explore.Application.DTOs.EventSessionTemplateSync;
using Explore.Domain.Settings.Definitions;
using FluentValidation;

namespace Explore.Application.DTOs.EventSessionTemplateSync.Validators;

public sealed class TemplateSyncPlanDtoValidator : AbstractValidator<TemplateSyncPlanDto>
{
    private static readonly int MaxChangeCount = int.Parse(CustomPropertyQuotaSettingDefinitions.SyncApplyMaxChangeCount.DefaultValue);

    public TemplateSyncPlanDtoValidator()
    {
        RuleFor(x => x.TargetTemplateVersion)
            .GreaterThan(0)
            .WithMessage("TargetTemplateVersion must be greater than 0.");

        RuleFor(x => x.BaseProvenanceVersion)
            .GreaterThanOrEqualTo(0)
            .WithMessage("BaseProvenanceVersion must be greater than or equal to 0.");

        RuleFor(x => x)
            .Must(x => x.GetTotalChangeCount() <= MaxChangeCount)
            .WithMessage($"Sync plan exceeds the maximum allowed change count of {MaxChangeCount}.");
    }
}
