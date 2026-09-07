using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.RegistrationOrders.Validators;

public sealed class GuestRegistrationOrderAccessCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : IGuestRegistrationOrderAccessCommand
{
    public GuestRegistrationOrderAccessCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.CapabilityToken).NotEmpty().MaximumLength(256);
    }
}

public sealed class AuthenticatedRegistrationOrderAccessCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : IAuthenticatedRegistrationOrderAccessCommand
{
    public AuthenticatedRegistrationOrderAccessCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.OrderId).NotEmpty();
    }
}
