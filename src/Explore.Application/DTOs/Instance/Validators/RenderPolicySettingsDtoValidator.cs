using Explore.Domain.Enums;
using FluentValidation;

namespace Explore.Application.DTOs.Instance.Validators;

public class RenderPolicySettingsDtoValidator : AbstractValidator<RenderPolicySettingsDto>
{
    public RenderPolicySettingsDtoValidator()
    {
        RuleFor(x => x.RenderPolicyVersion)
            .GreaterThan(0)
            .WithMessage("RenderPolicyVersion must be greater than 0.");

        RuleFor(x => x.RenderPolicyPreset)
            .Must(preset => Enum.TryParse<RenderPolicyPresetEnum>(preset, ignoreCase: true, out _))
            .WithMessage("RenderPolicyPreset must be SeoBalanced, AllPrerendered, AllInteractiveAutoNoPrerender, AllInteractiveServer, or CustomAdvanced.");

        RuleFor(x => x.GlobalRenderMode)
            .Must(IsValidRenderMode)
            .WithMessage("GlobalRenderMode must be InteractiveAuto, InteractiveWebAssembly, or InteractiveServer.");

        RuleFor(x => x.PublicSeoRenderMode)
            .Must(IsValidRenderMode)
            .WithMessage("PublicSeoRenderMode must be InteractiveAuto, InteractiveWebAssembly, or InteractiveServer.");

        RuleFor(x => x.OperationalRenderMode)
            .Must(IsValidRenderMode)
            .WithMessage("OperationalRenderMode must be InteractiveAuto, InteractiveWebAssembly, or InteractiveServer.");

        RuleFor(x => x.AdminRenderMode)
            .Must(IsValidRenderMode)
            .WithMessage("AdminRenderMode must be InteractiveAuto, InteractiveWebAssembly, or InteractiveServer.");

        RuleFor(x => x.OnboardingRenderMode)
            .Must(IsValidRenderMode)
            .WithMessage("OnboardingRenderMode must be InteractiveAuto, InteractiveWebAssembly, or InteractiveServer.");

        RuleFor(x => x.EnableAdvancedRenderPolicyOverrides)
            .Equal(true)
            .When(x => string.Equals(x.RenderPolicyPreset, "CustomAdvanced", StringComparison.OrdinalIgnoreCase))
            .WithMessage("EnableAdvancedRenderPolicyOverrides must be true when RenderPolicyPreset is CustomAdvanced.");

        RuleFor(x => x.EnableAdvancedRenderPolicyOverrides)
            .Equal(false)
            .When(x => !string.Equals(x.RenderPolicyPreset, "CustomAdvanced", StringComparison.OrdinalIgnoreCase))
            .WithMessage("EnableAdvancedRenderPolicyOverrides must be false for non-CustomAdvanced presets.");
    }

    private static bool IsValidRenderMode(string? mode)
    {
        return Enum.TryParse<RenderModeOptionEnum>(mode, ignoreCase: true, out _);
    }
}
