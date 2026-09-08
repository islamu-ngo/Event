using Explore.Application.Features.EmailDispatch.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.EmailDispatch.Validators;

public sealed class SetEmailDispatchTenantPauseStateCommandValidator : AbstractValidator<SetEmailDispatchTenantPauseStateCommand>
{
    public SetEmailDispatchTenantPauseStateCommandValidator()
    {
        RuleFor(command => command.TenantId)
            .NotEmpty()
            .WithMessage("TenantId is required.");

        RuleFor(command => command.PauseReason)
            .MaximumLength(500)
            .WithMessage("Pause reason must be 500 characters or fewer.");
    }
}
