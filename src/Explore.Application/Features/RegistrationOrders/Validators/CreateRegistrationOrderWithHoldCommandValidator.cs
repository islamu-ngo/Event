using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.RegistrationOrders.Validators;

public sealed class CreateRegistrationOrderWithHoldCommandValidator : AbstractValidator<CreateRegistrationOrderWithHoldCommand>
{
    public CreateRegistrationOrderWithHoldCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.TicketCatalogVersionId).NotEmpty();
        RuleFor(command => command.BookingPartyType).IsInEnum();
        RuleFor(command => command.PlatformContributionBasisPoints)
            .InclusiveBetween(0, 10_000)
            .When(command => command.PlatformContributionBasisPoints.HasValue);
        RuleFor(command => command.Lines).NotEmpty();
        RuleForEach(command => command.Lines).ChildRules(line =>
        {
            line.RuleFor(selection => selection.TicketTypeId).NotEmpty();
            line.RuleFor(selection => selection.Quantity).GreaterThan(0);
        });
        RuleFor(command => command.Lines)
            .Must(lines => lines.Select(line => line.TicketTypeId).Distinct().Count() == lines.Count)
            .WithMessage("Ticket selections must be unique.");
        RuleFor(command => command.VerifiedContactNormalizedEmail)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(320)
            .When(command => command.VerifiedContactNormalizedEmail is not null);
    }
}
