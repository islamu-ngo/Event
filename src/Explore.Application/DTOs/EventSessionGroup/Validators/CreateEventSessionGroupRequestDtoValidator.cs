using Explore.Application.Contracts.Persistence;
using FluentValidation;

namespace Explore.Application.DTOs.EventSessionGroup.Validators;

public class CreateEventSessionGroupRequestDtoValidator : AbstractValidator<CreateEventSessionGroupRequestDto>
{
    public CreateEventSessionGroupRequestDtoValidator(
        IEventRepository eventRepository,
        ILocationRepository locationRepository,
        ILocationRoomRepository locationRoomRepository)
    {
        RuleFor(request => request.EventId)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MustAsync(async (id, cancellationToken) => await eventRepository.Exists(id))
            .WithMessage("Event does not exist.");

        RuleFor(request => request.Name)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MaximumLength(200).WithMessage("{PropertyName} must not exceed 200 characters.");

        RuleFor(request => request.Slug)
            .MaximumLength(200).WithMessage("{PropertyName} must not exceed 200 characters.");

        RuleFor(request => request.Description)
            .MaximumLength(2000).WithMessage("{PropertyName} must not exceed 2000 characters.");

        RuleFor(request => request.Color)
            .MaximumLength(32).WithMessage("{PropertyName} must not exceed 32 characters.");

        RuleFor(request => request.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("{PropertyName} must be non-negative.");

        RuleFor(request => request.LocationId)
            .MustAsync(async (id, cancellationToken) => !id.HasValue || await locationRepository.Exists(id.Value))
            .WithMessage("Location does not exist.");

        RuleFor(request => request.RoomId)
            .MustAsync(async (id, cancellationToken) => !id.HasValue || await locationRoomRepository.Exists(id.Value))
            .WithMessage("Room does not exist.");

        RuleFor(request => request)
            .MustAsync(async (request, cancellationToken) =>
            {
                if (!request.RoomId.HasValue)
                    return true;

                var room = await locationRoomRepository.GetById(request.RoomId.Value);
                if (room is null)
                    return true;

                return !request.LocationId.HasValue || room.LocationId == request.LocationId.Value;
            })
            .WithMessage("Room must belong to the selected location.");
    }
}
