using System.Text.Json;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using FluentValidation;

namespace Explore.Application.DTOs.Instance.Validators;

public sealed class LocationPrivacyGovernanceSettingsDtoValidator
    : AbstractValidator<LocationPrivacyGovernanceSettingsDto>
{
    public LocationPrivacyGovernanceSettingsDtoValidator()
    {
        RuleFor(settings => settings.MinimumHomeAudience)
            .Must(value => IsValid(
                GovernanceSettingKeys.LocationPrivacy.MinimumHomeAudience,
                value))
            .WithMessage("MinimumHomeAudience must be NEVER, CONFIRMED_PARTICIPANT, or ANY_CURRENT_REGISTRANT.");

        RuleFor(settings => settings.DefaultRevealOffset)
            .Must(value => IsValid(
                GovernanceSettingKeys.LocationPrivacy.DefaultRevealOffset,
                value))
            .WithMessage("DefaultRevealOffset must be an ISO-8601 duration from PT0S through P30D.");
    }

    private static bool IsValid(string key, string? value) =>
        value is not null
        && LocationPrivacyGovernancePolicy.TryParse(
            key,
            JsonSerializer.Serialize(value),
            out _,
            out _);
}
