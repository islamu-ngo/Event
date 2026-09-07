using Explore.Application.Features.Promotions.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.Promotions.Validators;

public sealed class ApplyPromotionCodeToRegistrationOrderCommandValidator : AbstractValidator<ApplyPromotionCodeToRegistrationOrderCommand>
{
    public ApplyPromotionCodeToRegistrationOrderCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.Code).NotEmpty().MaximumLength(128);
    }
}

public sealed class RemovePromotionFromRegistrationOrderCommandValidator : AbstractValidator<RemovePromotionFromRegistrationOrderCommand>
{
    public RemovePromotionFromRegistrationOrderCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
    }
}
