using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionGroup.Validators;
using Explore.Application.Features.EventSessionGroups.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;

namespace Explore.Application.Features.EventSessionGroups.Handlers.Commands;

public class AssignSessionToGroupCommandHandler : ICommandHandler<AssignSessionToGroupCommand, BaseCommandResponse<Guid>>
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

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(AssignSessionToGroupCommand command, CancellationToken cancellationToken = default)
    {
        var validator = new AssignSessionToGroupRequestDtoValidator(
            _eventRepository,
            _eventSessionGroupRepository,
            _eventSessionRepository);
        var validationResult = await validator.ValidateAsync(command.Assignment, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage),
                "Session group assignment failed.");
        }

        var group = await _eventSessionGroupRepository.GetForUpdateAsync(
            command.Assignment.EventSessionGroupId,
            cancellationToken);
        var session = await _eventSessionRepository.GetById(command.Assignment.EventSessionId);
        var parentEvent = await _eventRepository.GetById(command.Assignment.EventId);

        if (group is null || session is null || parentEvent is null)
        {
            return BaseCommandResponse.NotFound<Guid>(
                "Event, session group, or event session was not found in the current tenant.");
        }

        if (group.EventId != command.Assignment.EventId || session.EventId != command.Assignment.EventId)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Session group and event session must belong to the requested event."],
                "Session group and event session must belong to the requested event.");
        }

        var existingAssignment = await _eventSessionGroupSessionRepository.GetExistingAssignmentAsync(
            command.Assignment.EventSessionGroupId,
            command.Assignment.EventSessionId,
            cancellationToken);

        if (command.Assignment.IsPrimary)
        {
            await DemoteOtherPrimaryAssignmentsAsync(session.Id, existingAssignment?.Id, cancellationToken);
        }

        if (existingAssignment is not null)
        {
            existingAssignment.EventId = command.Assignment.EventId;
            existingAssignment.IsPrimary = command.Assignment.IsPrimary;
            existingAssignment.SortOrder = command.Assignment.SortOrder;
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
            IsPrimary = command.Assignment.IsPrimary,
            SortOrder = command.Assignment.SortOrder
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
