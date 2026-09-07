using Explore.Application.DTOs.RegistrationOrders;
using FluentValidation;

namespace Explore.Application.Features.RegistrationOrders.Validators;

public sealed class RegistrationMaterialChangeChoiceRequestDtoValidator
    : AbstractValidator<RegistrationMaterialChangeChoiceRequestDto>
{
    public RegistrationMaterialChangeChoiceRequestDtoValidator()
    {
        RuleFor(value => value.CampaignId).NotEmpty();
        RuleFor(value => value.ChoiceCode)
            .Must(value => value is "accept_new_terms" or "request_refund");
    }
}
