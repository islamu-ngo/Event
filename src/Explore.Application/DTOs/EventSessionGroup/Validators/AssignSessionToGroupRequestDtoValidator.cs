using Explore.Application.Contracts.Persistence;
using FluentValidation;

namespace Explore.Application.DTOs.EventSessionGroup.Validators;

public class AssignSessionToGroupRequestDtoValidator : AbstractValidator<AssignSessionToGroupRequestDto>
{
    public AssignSessionToGroupRequestDtoValidator(
        IEventRepository eventRepository,
        IEventSessionGroupRepository eventSessionGroupRepository,
        IEventSessionRepository eventSessionRepository)
    {
        RuleFor(request => request.EventId)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MustAsync(async (id, cancellationToken) => await eventRepository.Exists(id))
            .WithMessage("Event does not exist.");

        RuleFor(request => request.EventSessionGroupId)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MustAsync(async (id, cancellationToken) => await eventSessionGroupRepository.Exists(id))
            .WithMessage("Event session group does not exist.");

        RuleFor(request => request.EventSessionId)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MustAsync(async (id, cancellationToken) => await eventSessionRepository.Exists(id))
            .WithMessage("Event session does not exist.");

        RuleFor(request => request.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("{PropertyName} must be non-negative.");
    }
}
