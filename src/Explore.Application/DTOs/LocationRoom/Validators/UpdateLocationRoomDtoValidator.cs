using Explore.Application.Contracts.Persistence;
using FluentValidation;

namespace Explore.Application.DTOs.LocationRoom.Validators;

public class UpdateLocationRoomDtoValidator : AbstractValidator<UpdateLocationRoomDto>
{
    public UpdateLocationRoomDtoValidator(ILocationRepository locationRepository)
    {
        RuleFor(dto => dto.Location!)
            .SetValidator(new UpdateLocationRoomLocationDtoValidator(locationRepository))
            .When(dto => dto.Location is not null);

        RuleFor(dto => dto.Name!)
            .SetValidator(new UpdateLocationRoomNameDtoValidator())
            .When(dto => dto.Name is not null);

        RuleFor(dto => dto.Slug!)
            .SetValidator(new UpdateLocationRoomSlugDtoValidator())
            .When(dto => dto.Slug is not null);

        RuleFor(dto => dto.Description!)
            .SetValidator(new UpdateLocationRoomDescriptionDtoValidator())
            .When(dto => dto.Description is not null);

        RuleFor(dto => dto.Capacity!)
            .SetValidator(new UpdateLocationRoomCapacityDtoValidator())
            .When(dto => dto.Capacity is not null);

        RuleFor(dto => dto.SortOrder!)
            .SetValidator(new UpdateLocationRoomSortOrderDtoValidator())
            .When(dto => dto.SortOrder is not null);

        RuleFor(dto => dto)
            .Must(HasAnyGroup)
            .WithMessage("At least one location room update group must be provided.");
    }

    private static bool HasAnyGroup(UpdateLocationRoomDto dto) =>
        dto.Location is not null ||
        dto.Name is not null ||
        dto.Slug is not null ||
        dto.Description is not null ||
        dto.Capacity is not null ||
        dto.SortOrder is not null;
}

public class UpdateLocationRoomLocationDtoValidator : AbstractValidator<UpdateLocationRoomLocationDto>
{
    public UpdateLocationRoomLocationDtoValidator(ILocationRepository locationRepository)
    {
        RuleFor(dto => dto.LocationId)
            .NotEmpty().WithMessage("LocationId is required.")
            .MustAsync(async (id, cancellationToken) => await locationRepository.Exists(id))
            .WithMessage("Location does not exist.");
    }
}

public class UpdateLocationRoomNameDtoValidator : AbstractValidator<UpdateLocationRoomNameDto>
{
    public UpdateLocationRoomNameDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");
    }
}

public class UpdateLocationRoomSlugDtoValidator : AbstractValidator<UpdateLocationRoomSlugDto>
{
    public UpdateLocationRoomSlugDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("Slug group must include Value.");

        RuleFor(dto => dto.Value.Value)
            .NotEmpty().WithMessage("Slug must not be blank. Use null to clear it.")
            .MaximumLength(200).WithMessage("Slug must not exceed 200 characters.")
            .When(dto => dto.Value.HasValue && dto.Value.Value is not null);
    }
}

public class UpdateLocationRoomDescriptionDtoValidator : AbstractValidator<UpdateLocationRoomDescriptionDto>
{
    public UpdateLocationRoomDescriptionDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("Description group must include Value.");

        RuleFor(dto => dto.Value.Value)
            .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters.")
            .When(dto => dto.Value.HasValue && dto.Value.Value is not null);
    }
}

public class UpdateLocationRoomCapacityDtoValidator : AbstractValidator<UpdateLocationRoomCapacityDto>
{
    public UpdateLocationRoomCapacityDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("Capacity group must include Value.");

        RuleFor(dto => dto.Value.Value)
            .GreaterThanOrEqualTo(0)
            .When(dto => dto.Value.HasValue && dto.Value.Value.HasValue)
            .WithMessage("Capacity must be non-negative.");
    }
}

public class UpdateLocationRoomSortOrderDtoValidator : AbstractValidator<UpdateLocationRoomSortOrderDto>
{
    public UpdateLocationRoomSortOrderDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .GreaterThanOrEqualTo(0).WithMessage("SortOrder must be non-negative.");
    }
}
