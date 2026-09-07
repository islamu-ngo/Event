using Explore.Application.Features.EmailDispatch.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.EmailDispatch.Validators;

public sealed class ParkEmailDispatchCommandValidator : AbstractValidator<ParkEmailDispatchCommand>
{
    public ParkEmailDispatchCommandValidator()
    {
        RuleFor(command => command.TenantId)
            .NotEmpty()
            .WithMessage("TenantId is required.");

        RuleFor(command => command.OutboxId)
            .NotEmpty()
            .WithMessage("OutboxId is required.");

        RuleFor(command => command.Reason)
            .NotEmpty()
            .WithMessage("Park reason is required.")
            .MaximumLength(500)
            .WithMessage("Park reason must be 500 characters or fewer.");
    }
}
