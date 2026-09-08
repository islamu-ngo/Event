using Explore.Application.Features.EmailDispatch.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.EmailDispatch.Validators;

public sealed class ReplayEmailDispatchCommandValidator : AbstractValidator<ReplayEmailDispatchCommand>
{
    public ReplayEmailDispatchCommandValidator()
    {
        RuleFor(command => command.TenantId)
            .NotEmpty()
            .WithMessage("TenantId is required.");

        RuleFor(command => command.OutboxId)
            .NotEmpty()
            .WithMessage("OutboxId is required.");
    }
}
