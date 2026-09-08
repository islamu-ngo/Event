using Explore.Application.Features.EmailDispatch.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.EmailDispatch.Validators;

public sealed class SetEmailDispatchGlobalRateLimitOverrideCommandValidator
    : AbstractValidator<SetEmailDispatchGlobalRateLimitOverrideCommand>
{
    public SetEmailDispatchGlobalRateLimitOverrideCommandValidator()
    {
        RuleFor(command => command.RateLimitPerMinute)
            .InclusiveBetween(1, EmailDispatchProcessorControl.MaximumGlobalRateLimitPerMinute)
            .When(command => command.RateLimitPerMinute.HasValue)
            .WithMessage("Global SMTP rate-limit override must be between 1 and 100000 per minute.");
    }
}
