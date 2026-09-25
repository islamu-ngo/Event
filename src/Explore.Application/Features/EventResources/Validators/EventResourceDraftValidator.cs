using Explore.Application.DTOs.EventResource;
using Explore.Domain.Enums;
using FluentValidation;

namespace Explore.Application.Features.EventResources.Validators;

public sealed class EventResourceDraftValidator : AbstractValidator<EventResourceDraftDto>
{
    public const int MaximumAudienceRules = 100;

    public EventResourceDraftValidator()
    {
        RuleFor(value => value.Title).NotEmpty().MaximumLength(500);
        RuleFor(value => value.PublicTitle).MaximumLength(500);
        RuleFor(value => value.PublicTitle).NotEmpty().When(value => value.DisclosureMode != EventResourceDisclosureModeEnum.EligibleOnly);
        RuleFor(value => value.Description).MaximumLength(5000);
        RuleFor(value => value.SensitiveNotes).MaximumLength(5000);
        RuleFor(value => value.LanguageCode).MaximumLength(35);
        RuleFor(value => value.AccessibilityNote).MaximumLength(2000);
        RuleFor(value => value.SortOrder).GreaterThanOrEqualTo(0);
        RuleFor(value => value.Kind).IsInEnum();
        RuleFor(value => value.DisclosureMode).IsInEnum();
        RuleFor(value => value.DeliveryType).IsInEnum();
        RuleFor(value => value.EventSessionId).Must(value => value != Guid.Empty);
        RuleFor(value => value.AccessibleAlternativeEventResourceId).Must(value => value != Guid.Empty);
        RuleFor(value => value.AudienceRules).Must(rules => !rules.IsDefaultOrEmpty
            && rules.Length <= MaximumAudienceRules && rules.All(rule => rule is not null)
            && (rules.Length == 1 || rules.All(rule => rule.Kind != EventResourceAudienceKindEnum.Public)))
            .WithMessage("A bounded, nonempty audience policy is required.");
        RuleFor(value => value.Availability).Must(ValidTimeIntent)
            .WithMessage("Availability boundaries must specify valid absolute or relative intent.");
    }

    private static bool ValidTimeIntent(EventResourceTimeIntentDto? intent)
    {
        if (intent is null) return false;
        try { _ = intent.ToDomain(); return true; }
        catch (ArgumentException) { return false; }
    }
}
