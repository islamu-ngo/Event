using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Admissions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using FluentValidation;
using MediatR;

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
    IRequestHandler<
        CompleteParticipantAdmissionCommand,
        BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        CompleteParticipantAdmissionCommand request,
        CancellationToken cancellationToken)
    {
        var validation =
            await new ParticipantAdmissionCommandValidator<
                    CompleteParticipantAdmissionCommand>()
                .ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validation.Errors.Select(
                    error => error.ErrorMessage),
                id: request.RegistrationTicketAssignmentId);
        }
        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid subjectUserId)
        {
            return Failure(
                ParticipantAdmissionFailureCodes
                    .SubjectAuthorityRequired,
                request.RegistrationTicketAssignmentId);
        }

        return await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                ParticipantAdmissionCompletionContext? context =
                    await repository
                        .LoadCompletionForUpdateAsync(
                            tenant.TenantId,
                            request.EventId,
                            request.RegistrationOrderId,
                            request
                                .RegistrationTicketAssignmentId,
                            request.ParticipantId,
                            subjectUserId,
                            token);
                if (context is null)
                {
                    return Failure(
                        ParticipantAdmissionFailureCodes
                            .ParticipantUnavailable,
                        request
                            .RegistrationTicketAssignmentId);
                }
                if (!context.RequirementsComplete)
                {
                    return Failure(
                        ParticipantAdmissionFailureCodes
                            .CompletionEvidenceIncomplete,
                        request
                            .RegistrationTicketAssignmentId);
                }
                if (context.Eligibility.ConsentRequired
                    && !context.SubjectConsentRecordId.HasValue)
                {
                    return Failure(
                        ParticipantAdmissionFailureCodes
                            .ConsentEvidenceRequired,
                        request
                            .RegistrationTicketAssignmentId);
                }
                if (context.Eligibility.RevokedAt.HasValue)
                {
                    return Failure(
                        ParticipantAdmissionFailureCodes
                            .AdmissionRevoked,
                        request
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

public sealed class ApproveParticipantAdmissionCommandHandler(
    IParticipantAdmissionEligibilityRepository repository,
    IActorRepository actors,
    ICurrentUserService currentUser,
    ITenantContext tenant,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) :
    IRequestHandler<
        ApproveParticipantAdmissionCommand,
        BaseCommandResponse<Guid>>
{
    public Task<BaseCommandResponse<Guid>> Handle(
        ApproveParticipantAdmissionCommand request,
        CancellationToken cancellationToken) =>
        ParticipantAdmissionDecisionHandler.ExecuteAsync(
            request,
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
    IRequestHandler<
        RevokeParticipantAdmissionCommand,
        BaseCommandResponse<Guid>>
{
    public Task<BaseCommandResponse<Guid>> Handle(
        RevokeParticipantAdmissionCommand request,
        CancellationToken cancellationToken) =>
        ParticipantAdmissionDecisionHandler.ExecuteAsync(
            request,
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
