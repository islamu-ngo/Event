using Explore.Application.Contracts.Persistence;
using FluentValidation;

namespace Explore.Application.DTOs.LocationRoom.Validators;

public class CreateLocationRoomDtoValidator : AbstractValidator<CreateLocationRoomDto>
{
    public CreateLocationRoomDtoValidator(ILocationRepository locationRepository)
    {
        RuleFor(d => d.LocationId)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MustAsync(async (id, ct) => await locationRepository.Exists(id))
            .WithMessage("Location does not exist.");

        RuleFor(d => d.Name)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MaximumLength(200).WithMessage("{PropertyName} must not exceed 200 characters.");

        RuleFor(d => d.Slug)
            .MaximumLength(200).WithMessage("{PropertyName} must not exceed 200 characters.");

        RuleFor(d => d.Description)
            .MaximumLength(2000).WithMessage("{PropertyName} must not exceed 2000 characters.");

        RuleFor(d => d.Capacity)
            .GreaterThanOrEqualTo(0).When(d => d.Capacity.HasValue)
            .WithMessage("{PropertyName} must be non-negative.");

        RuleFor(d => d.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("{PropertyName} must be non-negative.");
    }
}
