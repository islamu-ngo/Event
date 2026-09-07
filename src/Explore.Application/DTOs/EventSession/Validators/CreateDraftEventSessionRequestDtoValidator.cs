using Explore.Application.DTOs.EventSession;
using FluentValidation;

namespace Explore.Application.DTOs.EventSession.Validators;

public sealed class CreateDraftEventSessionRequestDtoValidator : AbstractValidator<CreateDraftEventSessionRequestDto>
{
    public CreateDraftEventSessionRequestDtoValidator()
    {
        RuleFor(x => x.EventId)
            .NotEqual(Guid.Empty)
            .WithMessage("EventId is required.");

        RuleFor(x => x.Title)
            .NotEmpty()
            .WithMessage("Title is required.")
            .MaximumLength(500)
            .WithMessage("Title must not exceed 500 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(5000)
            .When(x => !string.IsNullOrWhiteSpace(x.Description))
            .WithMessage("Description must not exceed 5000 characters.");

        RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0)
            .WithMessage("SortOrder must be non-negative.");
    }
}
