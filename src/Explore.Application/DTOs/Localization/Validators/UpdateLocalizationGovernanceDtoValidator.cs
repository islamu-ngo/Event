namespace Explore.Application.DTOs.Localization.Validators;

using Explore.Domain.Common.Localization;
using FluentValidation;

public class UpdateLocalizationGovernanceDtoValidator : AbstractValidator<UpdateLocalizationGovernanceDto>
{
    private static readonly string[] AllowedProviders = ["none", "tolgee", "weblate"];

    public UpdateLocalizationGovernanceDtoValidator()
    {
        RuleFor(x => x)
            .Must(x => x.Tms is not null || x.Languages is not null || x.Runtime is not null)
            .WithMessage("At least one localization governance group is required.");

        RuleFor(x => x.Tms!.Provider)
            .NotEmpty().WithMessage("TmsProvider is required.")
            .Must(p => AllowedProviders.Contains(p.Trim().ToLowerInvariant()))
            .WithMessage("TmsProvider must be one of: none, tolgee, weblate.")
            .When(x => x.Tms is not null);

        RuleFor(x => x.Tms!.ApiUrl)
            .NotEmpty().WithMessage("TmsApiUrl is required when a TMS provider is selected.")
            .Must(BeValidAbsoluteUrl).WithMessage("TmsApiUrl must be an absolute http(s) URL.")
            .When(x => x.Tms is not null && !string.Equals(x.Tms.Provider, "none", StringComparison.OrdinalIgnoreCase));

        RuleFor(x => x.Tms!.ProjectId)
            .NotEmpty().WithMessage("TmsProjectId is required when a TMS provider is selected.")
            .When(x => x.Tms is not null && !string.Equals(x.Tms.Provider, "none", StringComparison.OrdinalIgnoreCase));

        RuleFor(x => x.Tms!.Component)
            .NotEmpty().WithMessage("TmsComponent (component slug) is required for Weblate.")
            .When(x => string.Equals(x.Tms?.Provider, "weblate", StringComparison.OrdinalIgnoreCase));

        RuleFor(x => x.Languages!.EnabledLanguages)
            .NotNull().WithMessage("EnabledLanguages is required.")
            .Must(list => list is { Count: > 0 })
            .WithMessage("At least one language must be enabled.")
            .Must(list => list.All(code => CultureRegistry.Contains(code)))
            .WithMessage("EnabledLanguages contains codes that are not in CultureRegistry.")
            .When(x => x.Languages is not null);

        RuleFor(x => x.Languages!.FallbackLanguage)
            .NotEmpty().WithMessage("FallbackLanguage is required.")
            .Must(code => CultureRegistry.Contains(code))
            .WithMessage("FallbackLanguage must be a supported culture code.")
            .When(x => x.Languages is not null);

        RuleFor(x => x)
            .Must(dto => dto.Languages is null || dto.Languages.EnabledLanguages.Contains(dto.Languages.FallbackLanguage, StringComparer.OrdinalIgnoreCase))
            .WithMessage("FallbackLanguage must be present in EnabledLanguages.")
            .WithName("FallbackLanguage");

        RuleFor(x => x)
            .Must(dto => dto.Languages is null || dto.Languages.EnabledLanguages.Contains(dto.Languages.DefaultLanguage, StringComparer.OrdinalIgnoreCase))
            .WithMessage("DefaultLanguage must be present in EnabledLanguages.")
            .WithName("DefaultLanguage");

        RuleFor(x => x.Languages!.DefaultLanguage)
            .NotEmpty().WithMessage("DefaultLanguage is required.")
            .Must(code => CultureRegistry.Contains(code))
            .WithMessage("DefaultLanguage must be a supported culture code.")
            .When(x => x.Languages is not null);
    }

    private static bool BeValidAbsoluteUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
}
