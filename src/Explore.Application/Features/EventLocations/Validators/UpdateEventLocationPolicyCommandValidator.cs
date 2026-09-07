using Explore.Application.Features.EventLocations.Requests.Commands;
using Explore.Domain.Enums;
using FluentValidation;

namespace Explore.Application.Features.EventLocations.Validators;

public sealed class UpdateEventLocationPolicyCommandValidator
    : AbstractValidator<UpdateEventLocationPolicyCommand>
{
    public UpdateEventLocationPolicyCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.EventLocationId).NotEmpty();
        RuleFor(command => command.ExpectedConcurrencyStamp).NotEmpty();
        RuleFor(command => command.ExpectedPolicyVersion)
            .GreaterThan(0)
            .LessThan(int.MaxValue);
        RuleFor(command => command)
            .Must(command => command.Fields is not null || command.Audience is not null)
            .WithMessage("At least one EventLocation disclosure group is required.");
        RuleFor(command => command.Fields)
            .Must(fields => fields is null ||
                fields.ShowVenueName.HasValue ||
                fields.ShowCity.HasValue ||
                fields.ShowCountry.HasValue ||
                fields.ShowRoomName.HasValue ||
                fields.ShowStreetAddress.HasValue ||
                fields.ShowPostcode.HasValue ||
                fields.ShowCoordinates.HasValue)
            .WithMessage("The EventLocation disclosure fields group must contain at least one supplied field.");
        RuleFor(command => command.Audience)
            .Must(audience => audience is null ||
                audience.FullDetailsAudienceId.HasValue ||
                audience.RevealFullDetailsFromUtc.HasValue)
            .WithMessage("The EventLocation disclosure audience group must contain at least one supplied field.");
        RuleFor(command => command.Audience!.FullDetailsAudienceId)
            .Must(value => !value.HasValue ||
                Enum.IsDefined(typeof(LocationDisclosureAudienceEnum), value.Value))
            .When(command => command.Audience is not null)
            .WithMessage("FullDetailsAudienceId is invalid.");
        RuleFor(command => command.Audience!.RevealFullDetailsFromUtc)
            .Must(update => !update.HasValue ||
                !update.Value.HasValue ||
                update.Value.Value.Kind == DateTimeKind.Utc)
            .When(command => command.Audience is not null)
            .WithMessage("RevealFullDetailsFromUtc must be UTC when provided.");
    }
}

public sealed class ConfirmEventLocationRemediationCommandValidator
    : AbstractValidator<ConfirmEventLocationRemediationCommand>
{
    public ConfirmEventLocationRemediationCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.EventLocationId).NotEmpty();
        RuleFor(command => command.ExpectedConcurrencyStamp).NotEmpty();
        RuleFor(command => command.ExpectedPolicyVersion)
            .GreaterThan(0)
            .LessThan(int.MaxValue);
    }
}
