using Explore.Application.DTOs.RegistrationOrders;
using FluentValidation;

namespace Explore.Application.Features.RegistrationOrders.Validators;

public sealed class RegistrationRefundRequestDtoValidator : AbstractValidator<RegistrationRefundRequestDto>
{
    private static readonly HashSet<string> Reasons = new(StringComparer.Ordinal)
    {
        "event_cancelled",
        "material_change",
        "customer_request",
        "duplicate_charge",
        "service_failure",
        "organizer_refund"
    };

    public RegistrationRefundRequestDtoValidator()
    {
        RuleFor(value => value.AmountMinor).GreaterThan(0).When(value => value.AmountMinor.HasValue);
        RuleFor(value => value.ReasonCode)
            .NotEmpty()
            .MaximumLength(80)
            .Must(value => Reasons.Contains(value));
    }
}
