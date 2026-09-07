using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.RegistrationOrders.Validators;

public sealed class RegistrationOrderLifecycleCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : IRegistrationOrderLifecycleCommand
{
    public RegistrationOrderLifecycleCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
    }
}
