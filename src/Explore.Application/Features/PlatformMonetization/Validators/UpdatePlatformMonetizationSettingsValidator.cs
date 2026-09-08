using Explore.Application.DTOs.PlatformMonetization;
using Explore.Application.Features.PlatformMonetization.Requests.Commands;
using Explore.Domain.ValueObjects;
using FluentValidation;

namespace Explore.Application.Features.PlatformMonetization.Validators;

public sealed class UpdatePlatformMonetizationSettingsValidator : AbstractValidator<UpdatePlatformMonetizationSettingsCommand>
{
    public UpdatePlatformMonetizationSettingsValidator()
    {
        RuleFor(command => command.Settings).NotNull();
        When(command => command.Settings is not null, () =>
        {
            RuleFor(command => command.Settings.ExpectedFeeVersion).GreaterThan(0);
            RuleFor(command => command.Settings.ExpectedContributionVersion).GreaterThan(0);
            RuleFor(command => command.Settings.FeeBasisPoints).InclusiveBetween(0, 10_000);
            RuleFor(command => command.Settings.FixedCharges)
                .Must(HasValidFixedCharges)
                .WithMessage("Fixed charges must use supported, monetary currencies with non-negative minor-unit amounts and no duplicate currencies.");
            RuleFor(command => command.Settings.ContributionOptions)
                .Must(HasValidContributionOptions)
                .WithMessage("Contribution options must contain exactly one zero-percent default with unique percentages and sort orders.");
            RuleFor(command => command.Settings.ContributionHeading).NotNull().MaximumLength(200);
            RuleFor(command => command.Settings.ContributionBody).NotNull().MaximumLength(2_000);
            When(command => command.Settings.ContributionEnabled, () =>
            {
                RuleFor(command => command.Settings.ContributionHeading).NotEmpty();
                RuleFor(command => command.Settings.ContributionBody).NotEmpty();
            });
        });
    }

    private static bool HasValidFixedCharges(IReadOnlyList<PlatformFeeFixedChargeDto>? charges)
    {
        if (charges is null)
        {
            return false;
        }

        var currencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PlatformFeeFixedChargeDto? charge in charges)
        {
            if (charge is null || string.IsNullOrWhiteSpace(charge.CurrencyCode) || charge.AmountMinor < 0)
            {
                return false;
            }

            try
            {
                CurrencyMetadata currency = CurrencyMetadata.Get(charge.CurrencyCode);
                if (currency.IsNoCurrency || !currencies.Add(currency.Code))
                {
                    return false;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasValidContributionOptions(IReadOnlyList<PlatformContributionOptionDto>? options)
    {
        if (options is null || options.Count == 0 || options.Any(option => option is null))
        {
            return false;
        }

        return options.Count(option => option.IsDefault) == 1
            && options.All(option => option.ContributionBasisPoints is >= 0 and <= 10_000
                && option.SortOrder >= 0
                && option.IsDefault == (option.ContributionBasisPoints == 0))
            && options.Select(option => option.ContributionBasisPoints).Distinct().Count() == options.Count
            && options.Select(option => option.SortOrder).Distinct().Count() == options.Count;
    }
}
