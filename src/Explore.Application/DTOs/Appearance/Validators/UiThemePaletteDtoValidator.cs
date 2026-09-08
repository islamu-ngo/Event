namespace Explore.Application.DTOs.Appearance.Validators;

using FluentValidation;

public class UiThemePaletteDtoValidator : AbstractValidator<UiThemePaletteDto>
{
    public UiThemePaletteDtoValidator()
    {
        RuleForPaletteHex(palette => palette.Primary, "Primary");
        RuleForPaletteHex(palette => palette.Secondary, "Secondary");
        RuleForPaletteHex(palette => palette.Background, "Background");
        RuleForPaletteHex(palette => palette.Surface, "Surface");
        RuleForPaletteHex(palette => palette.AppbarText, "Appbar text");
        RuleForPaletteHex(palette => palette.DrawerText, "Drawer text");
        RuleForPaletteHex(palette => palette.DrawerIcon, "Drawer icon");
        RuleForPaletteHex(palette => palette.TextPrimary, "Primary text");
        RuleForPaletteHex(palette => palette.TextSecondary, "Secondary text");
        RuleForPaletteHex(palette => palette.Info, "Info");
        RuleForPaletteHex(palette => palette.Success, "Success");
        RuleForPaletteHex(palette => palette.Warning, "Warning");
        RuleForPaletteHex(palette => palette.Error, "Error");
        RuleForPaletteHex(palette => palette.LinesDefault, "Lines default");

        RuleForPaletteFlexible(palette => palette.AppbarBackground, "Appbar background");
        RuleForPaletteFlexible(palette => palette.DrawerBackground, "Drawer background");
        RuleForPaletteFlexible(palette => palette.Divider, "Divider");
    }

    private void RuleForPaletteHex(System.Linq.Expressions.Expression<Func<UiThemePaletteDto, string>> selector, string displayName)
    {
        RuleFor(selector)
            .NotEmpty().WithMessage($"{displayName} is required.")
            .Must(UiThemeInputRules.IsHexColor).WithMessage($"{displayName} must be a #RRGGBB color.");
    }

    private void RuleForPaletteFlexible(System.Linq.Expressions.Expression<Func<UiThemePaletteDto, string>> selector, string displayName)
    {
        RuleFor(selector)
            .NotEmpty().WithMessage($"{displayName} is required.")
            .Must(UiThemeInputRules.IsHexOrRgbaColor).WithMessage($"{displayName} must be a #RRGGBB or rgba(...) color.");
    }
}
