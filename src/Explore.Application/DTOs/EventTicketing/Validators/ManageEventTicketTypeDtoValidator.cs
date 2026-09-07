using FluentValidation;

namespace Explore.Application.DTOs.EventTicketing.Validators;

public sealed class ManageEventTicketTypeDtoValidator : AbstractValidator<ManageEventTicketTypeDto>
{
    public ManageEventTicketTypeDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Entitlements).NotEmpty();
    }
}
