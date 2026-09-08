using Explore.Application.Features.EmailDispatch.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.EmailDispatch.Validators;

public sealed class ResolveEmailDispatchWithoutReplayCommandValidator
    : AbstractValidator<ResolveEmailDispatchWithoutReplayCommand>
{
    public ResolveEmailDispatchWithoutReplayCommandValidator()
    {
        RuleFor(command => command.TenantId)
            .NotEmpty()
            .WithMessage("TenantId is required.");

        RuleFor(command => command.OutboxId)
            .NotEmpty()
            .WithMessage("OutboxId is required.");

        RuleFor(command => command.Reason)
            .NotEmpty()
            .WithMessage("Resolution reason is required.")
            .MaximumLength(500)
            .WithMessage("Resolution reason must be 500 characters or fewer.");
    }
}
