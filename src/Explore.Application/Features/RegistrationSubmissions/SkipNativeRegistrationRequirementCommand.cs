using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationSubmissions;
using Explore.Domain;
using Explore.Domain.Enums;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.RegistrationSubmissions.Commands;

public sealed record SkipNativeRegistrationRequirementCommand(
    Guid TenantId,
    Guid EventId,
    Guid OrderId,
    Guid RequirementId,
    Guid AttemptId,
    string? AttemptCapabilityToken) : IRequest<NativeRegistrationSkipResult>;

public sealed record NativeRegistrationSkipResult(
    bool Success,
    NativeRegistrationRequirementProgressDto? Progress,
    string? FailureCode = null);

public sealed class SkipNativeRegistrationRequirementCommandValidator
    : AbstractValidator<SkipNativeRegistrationRequirementCommand>
{
    public SkipNativeRegistrationRequirementCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.RequirementId).NotEmpty();
        RuleFor(command => command.AttemptId).NotEmpty();
        RuleFor(command => command.AttemptCapabilityToken).NotEmpty();
    }
}

public sealed class SkipNativeRegistrationRequirementCommandHandler(
    IRegistrationInventoryRepository inventory,
    IRegistrationSubmissionRepository submissions,
    IRegistrationParticipantRepository participantRepository,
    IRegistrationFinalizationRepository finalization,
    IGuestCapabilityTokenService capabilities,
    TimeProvider timeProvider)
    : IRequestHandler<SkipNativeRegistrationRequirementCommand, NativeRegistrationSkipResult>
{
    public async Task<NativeRegistrationSkipResult> Handle(
        SkipNativeRegistrationRequirementCommand request,
        CancellationToken cancellationToken)
    {
        if (!(await new SkipNativeRegistrationRequirementCommandValidator()
                .ValidateAsync(request, cancellationToken)).IsValid)
        {
            return Missing();
        }

        RegistrationOrder? order = await inventory.GetOrderWithLinesAsync(
            request.OrderId, request.TenantId, cancellationToken);
        RegistrationAttempt? attempt = await submissions.GetAttemptAsync(
            request.TenantId, request.AttemptId, cancellationToken);
        RegistrationRequirement? requirement = await submissions.GetRequirementAsync(
            request.TenantId, request.RequirementId, cancellationToken);
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        if (order is null || attempt is null || requirement is null ||
            order.EventId != request.EventId ||
            order.RegistrationOrderStatusId != (int)RegistrationOrderStatusEnum.AwaitingRequirements ||
            order.RegistrationWorkflowVersionId != attempt.RegistrationWorkflowId ||
            attempt.EventId != request.EventId ||
            attempt.RegistrationOrderId != request.OrderId ||
            attempt.RegistrationRequirementId != request.RequirementId ||
            requirement.RegistrationWorkflowId != attempt.RegistrationWorkflowId ||
            !requirement.CanSkip ||
            requirement.CriticalityId == (int)RegistrationRequirementCriticalityEnum.Required ||
            !attempt.CanAcceptSubmissionAt(now) ||
            !capabilities.Matches(request.AttemptCapabilityToken, attempt.CapabilityTokenHash))
        {
            return Missing();
        }

        IReadOnlyList<RegistrationParticipant> participants = await participantRepository.GetParticipantsByOrderAsync(
            order.Id, request.TenantId, cancellationToken);
        IReadOnlyList<RegistrationTicketAssignment> assignments = await participantRepository
            .GetAssignmentsWithParticipantsByOrderAsync(order.Id, request.TenantId, cancellationToken);
        IReadOnlyList<RegistrationRequirementFulfillment> fulfillments = await finalization.GetFulfillmentsAsync(
            request.TenantId, order.Id, requirement.Id, cancellationToken);
        IReadOnlyList<NativeRegistrationAnswerSubjectDto> subjects = NativeRegistrationAttemptContractBuilder.Subjects(
            order, requirement, participants, assignments, fulfillments);
        if (subjects.Count == 0 || subjects.All(value => value.IsCompleted || value.IsSkipped))
        {
            return Missing();
        }

        RegistrationRequirementFulfillment[] skippedFulfillments = [.. subjects
            .Where(value => !value.IsCompleted && !value.IsSkipped)
            .Select(subject => RegistrationRequirementFulfillment.CreateSkipped(
                order, requirement, subject.SubjectType, subject.SubjectId, now))];

        Guid expectedStamp = attempt.ConcurrencyStamp;
        attempt.Consume(now);
        if (!await finalization.TryRecordSkippedFulfillmentsAndConsumeAttemptAsync(
                attempt, expectedStamp, skippedFulfillments, now, cancellationToken))
        {
            return Missing();
        }

        IReadOnlyList<NativeRegistrationAnswerSubjectDto> skipped = subjects
            .Select(subject => subject.IsCompleted
                ? subject
                : subject with { IsSkipped = true })
            .ToArray();
        return new(true, NativeRegistrationAttemptContractBuilder.Progress(skipped));
    }

    private static NativeRegistrationSkipResult Missing() =>
        new(false, null, "registration_attempt_not_found");
}
