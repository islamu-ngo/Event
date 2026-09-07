using Explore.Application.DTOs.Event;
using FluentValidation;

namespace Explore.Application.DTOs.Event.Validators;

public sealed class ImportEventRequestDtoValidator : AbstractValidator<ImportEventRequestDto>
{
    public ImportEventRequestDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(200).WithMessage("Title must not exceed 200 characters.");

        RuleFor(x => x.OwnerActorId)
            .NotEqual(Guid.Empty).WithMessage("OwnerActorId is required.");

        RuleFor(x => x.ProvenanceSource)
            .NotEmpty().WithMessage("ProvenanceSource is required.")
            .MaximumLength(100).WithMessage("ProvenanceSource must not exceed 100 characters.");

        RuleFor(x => x.ProvenanceExternalId)
            .NotEmpty().WithMessage("ProvenanceExternalId is required.")
            .MaximumLength(200).WithMessage("ProvenanceExternalId must not exceed 200 characters.");

        RuleFor(x => x.VisibilityTypeId)
            .Must(id => id is null or > 0).WithMessage("VisibilityTypeId must be a positive integer when provided.");

        RuleFor(x => x.EventFormatId)
            .Must(id => id is null or > 0).WithMessage("EventFormatId must be a positive integer when provided.");

        RuleFor(x => x.EventTypeId)
            .Must(id => id is null or > 0).WithMessage("EventTypeId must be a positive integer when provided.");

        RuleFor(x => x.AudienceGenderId)
            .Must(id => id is null or > 0).WithMessage("AudienceGenderId must be a positive integer when provided.");

        RuleFor(x => x.AudienceAgeId)
            .Must(id => id is null or > 0).WithMessage("AudienceAgeId must be a positive integer when provided.");

        RuleFor(x => x.Timezone)
            .MaximumLength(100).WithMessage("Timezone must not exceed 100 characters.");

        RuleFor(x => x.ParticipationConfiguration)
            .NotNull().WithMessage("ParticipationConfiguration is required.")
            .SetValidator(new ConfigureEventParticipationDtoValidator());
    }
}
