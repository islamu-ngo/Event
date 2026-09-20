using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Admissions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using FluentValidation;

namespace Explore.Application.Features.Admissions.Handlers.Commands;

public sealed class ParticipantAdmissionCommandValidator<TCommand> :
    AbstractValidator<TCommand>
    where TCommand : IParticipantAdmissionCommand
{
    public ParticipantAdmissionCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.RegistrationOrderId).NotEmpty();
        RuleFor(command =>
                command.RegistrationTicketAssignmentId)
            .NotEmpty();
        RuleFor(command => command.ParticipantId).NotEmpty();
    }
}

public sealed class CompleteParticipantAdmissionCommandHandler(
    IParticipantAdmissionEligibilityRepository repository,
    ICurrentUserService currentUser,
    ITenantContext tenant,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) :
    ICommandHandler<
        CompleteParticipantAdmissionCommand,
        BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        CompleteParticipantAdmissionCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation =
            await new ParticipantAdmissionCommandValidator<
                    CompleteParticipantAdmissionCommand>()
                .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validation.Errors.Select(
                    error => error.ErrorMessage),
                id: command.RegistrationTicketAssignmentId);
        }
        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid subjectUserId)
        {
            return Failure(
                ParticipantAdmissionFailureCodes
                    .SubjectAuthorityRequired,
                command.RegistrationTicketAssignmentId);
        }

        return await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                ParticipantAdmissionCompletionContext? context =
                    await repository
                        .LoadCompletionForUpdateAsync(
                            tenant.TenantId,
                            command.EventId,
                            command.RegistrationOrderId,
                            command
                                .RegistrationTicketAssignmentId,
                            command.ParticipantId,
                            subjectUserId,
                            token);
                if (context is null)
                {
                    return Failure(
                        ParticipantAdmissionFailureCodes
                            .ParticipantUnavailable,
                        command
                            .RegistrationTicketAssignmentId);
                }
                if (!context.RequirementsComplete)
                {
                    return Failure(
                        ParticipantAdmissionFailureCodes
                            .CompletionEvidenceIncomplete,
                        command
                            .RegistrationTicketAssignmentId);
                }
                if (context.Eligibility.ConsentRequired
                    && !context.SubjectConsentRecordId.HasValue)
                {
                    return Failure(
                        ParticipantAdmissionFailureCodes
                            .ConsentEvidenceRequired,
                        command
                            .RegistrationTicketAssignmentId);
                }
                if (context.Eligibility.RevokedAt.HasValue)
                {
                    return Failure(
                        ParticipantAdmissionFailureCodes
                            .AdmissionRevoked,
                        command
                            .RegistrationTicketAssignmentId);
                }

                context.Participant.ClaimBy(
                    subjectUserId,
                    Guid.CreateVersion7());
                context.Eligibility.RecordSubjectCompletion(
                    context.Participant,
                    subjectUserId,
                    context.SubjectConsentRecordId,
                    timeProvider.GetUtcNow().UtcDateTime,
                    Guid.CreateVersion7());
                await repository.ApplyDecisionAsync(
                    context.Eligibility,
                    token);
                return BaseCommandResponse.Success(
                    command.RegistrationTicketAssignmentId);
            },
            cancellationToken);
    }

    private static BaseCommandResponse<Guid> Failure(
        string code,
        Guid assignmentId) =>
        BaseCommandResponse.Failure<Guid>(
            code,
            id: assignmentId);
}

public sealed class ApproveParticipantAdmissionCommandHandler(
    IParticipantAdmissionEligibilityRepository repository,
    IActorRepository actors,
    ICurrentUserService currentUser,
    ITenantContext tenant,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) :
    ICommandHandler<
        ApproveParticipantAdmissionCommand,
        BaseCommandResponse<Guid>>
{
    public Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ApproveParticipantAdmissionCommand command,
        CancellationToken cancellationToken = default) =>
        ParticipantAdmissionDecisionHandler.ExecuteAsync(
            command,
            approve: true,
            repository,
            actors,
            currentUser,
            tenant,
            unitOfWork,
            timeProvider,
            cancellationToken);
}

public sealed class RevokeParticipantAdmissionCommandHandler(
    IParticipantAdmissionEligibilityRepository repository,
    IActorRepository actors,
    ICurrentUserService currentUser,
    ITenantContext tenant,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) :
    ICommandHandler<
        RevokeParticipantAdmissionCommand,
        BaseCommandResponse<Guid>>
{
    public Task<BaseCommandResponse<Guid>> ExecuteAsync(
        RevokeParticipantAdmissionCommand command,
        CancellationToken cancellationToken = default) =>
        ParticipantAdmissionDecisionHandler.ExecuteAsync(
            command,
            approve: false,
            repository,
            actors,
            currentUser,
            tenant,
            unitOfWork,
            timeProvider,
            cancellationToken);
}

file static class ParticipantAdmissionDecisionHandler
{
    public static async Task<BaseCommandResponse<Guid>>
        ExecuteAsync<TCommand>(
            TCommand request,
            bool approve,
            IParticipantAdmissionEligibilityRepository repository,
            IActorRepository actors,
            ICurrentUserService currentUser,
            ITenantContext tenant,
            IUnitOfWork unitOfWork,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        where TCommand : IParticipantAdmissionCommand
    {
        var validation =
            await new ParticipantAdmissionCommandValidator<TCommand>()
                .ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validation.Errors.Select(
                    error => error.ErrorMessage),
                id: request.RegistrationTicketAssignmentId);
        }
        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return Failure(
                ParticipantAdmissionFailureCodes
                    .ApprovalUnavailable,
                request.RegistrationTicketAssignmentId);
        }
        Actor? actor =
            await actors.GetActorByUserIdAndTenantId(
                userId,
                tenant.TenantId,
                cancellationToken);
        if (actor is null)
        {
            return Failure(
                ParticipantAdmissionFailureCodes
                    .ApprovalUnavailable,
                request.RegistrationTicketAssignmentId);
        }

        return await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                ParticipantAdmissionEligibility? eligibility =
                    await repository.LoadForUpdateAsync(
                        tenant.TenantId,
                        request.RegistrationTicketAssignmentId,
                        token);
                if (eligibility is null
                    || eligibility.EventId != request.EventId
                    || eligibility.RegistrationOrderId !=
                    request.RegistrationOrderId
                    || eligibility.ParticipantId !=
                    request.ParticipantId)
                {
                    return Failure(
                        ParticipantAdmissionFailureCodes
                            .ParticipantUnavailable,
                        request
                            .RegistrationTicketAssignmentId);
                }

                DateTime now =
                    timeProvider.GetUtcNow().UtcDateTime;
                if (approve)
                {
                    if (eligibility.RevokedAt.HasValue)
                    {
                        return Failure(
                            ParticipantAdmissionFailureCodes
                                .AdmissionRevoked,
                            request
                                .RegistrationTicketAssignmentId);
                    }
                    eligibility.Approve(
                        actor.Id,
                        now,
                        Guid.CreateVersion7());
                }
                else
                {
                    eligibility.Revoke(
                        actor.Id,
                        now,
                        Guid.CreateVersion7());
                    AdmissionTicket? ticket =
                        await repository
                            .GetIssuedTicketForUpdateAsync(
                                tenant.TenantId,
                                request
                                    .RegistrationTicketAssignmentId,
                                token);
                    if (ticket is not null
                        && ticket.AdmissionTicketStatusId ==
                        (int)AdmissionTicketStatusEnum.Active)
                    {
                        ticket.TransitionTo(
                            AdmissionTicketStatusEnum.Revoked,
                            now);
                    }
                }

                await repository.ApplyDecisionAsync(
                    eligibility,
                    token);
                return BaseCommandResponse.Success(
                    request.RegistrationTicketAssignmentId);
            },
            cancellationToken);
    }

    private static BaseCommandResponse<Guid> Failure(
        string code,
        Guid assignmentId) =>
        BaseCommandResponse.Failure<Guid>(
            code,
            id: assignmentId);
}
