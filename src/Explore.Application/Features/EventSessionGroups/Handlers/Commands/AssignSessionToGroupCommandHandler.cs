using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionGroup.Validators;
using Explore.Application.Features.EventSessionGroups.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventSessionGroups.Handlers.Commands;

public class AssignSessionToGroupCommandHandler : IRequestHandler<AssignSessionToGroupCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionGroupRepository _eventSessionGroupRepository;
    private readonly IEventSessionGroupSessionRepository _eventSessionGroupSessionRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IEventSessionRepository _eventSessionRepository;

    public AssignSessionToGroupCommandHandler(
        IEventSessionGroupRepository eventSessionGroupRepository,
        IEventSessionGroupSessionRepository eventSessionGroupSessionRepository,
        IEventRepository eventRepository,
        IEventSessionRepository eventSessionRepository)
    {
        _eventSessionGroupRepository = eventSessionGroupRepository;
        _eventSessionGroupSessionRepository = eventSessionGroupSessionRepository;
        _eventRepository = eventRepository;
        _eventSessionRepository = eventSessionRepository;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(AssignSessionToGroupCommand request, CancellationToken cancellationToken)
    {
        var validator = new AssignSessionToGroupRequestDtoValidator(
            _eventRepository,
            _eventSessionGroupRepository,
            _eventSessionRepository);
        var validationResult = await validator.ValidateAsync(request.Assignment, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage),
                "Session group assignment failed.");
        }

        var group = await _eventSessionGroupRepository.GetForUpdateAsync(
            request.Assignment.EventSessionGroupId,
            cancellationToken);
        var session = await _eventSessionRepository.GetById(request.Assignment.EventSessionId);
        var parentEvent = await _eventRepository.GetById(request.Assignment.EventId);

        if (group is null || session is null || parentEvent is null)
        {
            return BaseCommandResponse.NotFound<Guid>(
                "Event, session group, or event session was not found in the current tenant.");
        }

        if (group.EventId != request.Assignment.EventId || session.EventId != request.Assignment.EventId)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Session group and event session must belong to the requested event."],
                "Session group and event session must belong to the requested event.");
        }

        var existingAssignment = await _eventSessionGroupSessionRepository.GetExistingAssignmentAsync(
            request.Assignment.EventSessionGroupId,
            request.Assignment.EventSessionId,
            cancellationToken);

        if (request.Assignment.IsPrimary)
        {
            await DemoteOtherPrimaryAssignmentsAsync(session.Id, existingAssignment?.Id, cancellationToken);
        }

        if (existingAssignment is not null)
        {
            existingAssignment.EventId = request.Assignment.EventId;
            existingAssignment.IsPrimary = request.Assignment.IsPrimary;
            existingAssignment.SortOrder = request.Assignment.SortOrder;
            await _eventSessionGroupSessionRepository.Update(existingAssignment);

            return BaseCommandResponse.Success(
                existingAssignment.Id,
                "Session group assignment updated successfully.");
        }

        var assignment = new EventSessionGroupSession
        {
            EventSessionGroupId = group.Id,
            EventSessionGroup = null!,
            EventSessionId = session.Id,
            EventSession = null!,
            EventId = parentEvent.Id,
            Event = null!,
            TenantId = parentEvent.TenantId,
            Tenant = null!,
            IsPrimary = request.Assignment.IsPrimary,
            SortOrder = request.Assignment.SortOrder
        };

        assignment = await _eventSessionGroupSessionRepository.Create(assignment);

        return BaseCommandResponse.Success(
            assignment.Id,
            "Session group assignment created successfully.");
    }

    private async Task DemoteOtherPrimaryAssignmentsAsync(
        Guid eventSessionId,
        Guid? excludeAssignmentId,
        CancellationToken cancellationToken)
    {
        var primaryAssignments = await _eventSessionGroupSessionRepository.GetPrimaryAssignmentsForSessionAsync(
            eventSessionId,
            excludeAssignmentId,
            cancellationToken);

        foreach (var assignment in primaryAssignments)
        {
            assignment.IsPrimary = false;
            await _eventSessionGroupSessionRepository.Update(assignment);
        }
    }
}
