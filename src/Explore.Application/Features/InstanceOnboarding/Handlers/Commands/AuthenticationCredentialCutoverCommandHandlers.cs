using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using FluentValidation;
using ApplicationExceptions = Explore.Application.Exceptions;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

internal static class AuthenticationCredentialCutoverHandlerSupport
{
    public static async Task<(Guid InstanceId, Guid ActorId)> RequireCurrentInstanceAsync(
        IUserContext userContext,
        IAdminContext adminContext,
        IInstanceBootstrapStateRepository bootstrapRepository,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated
            || await adminContext.ResolveUserIdAsync(cancellationToken) is not Guid actorId
            || actorId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("authentication_cutover_operator_required");
        }
        if (!await adminContext.IsInstanceAdminAsync(cancellationToken))
            throw new ApplicationExceptions.AuthorizationException(
                "Current instance administrator authority is required.");

        var bootstrap = await bootstrapRepository.GetCurrent(cancellationToken);
        if (bootstrap is null || bootstrap.Status != InstanceBootstrapStatus.Completed)
        {
            throw new ApplicationExceptions.AuthorizationException(
                "Current completed instance bootstrap is required.");
        }

        return (bootstrap.Id, actorId);
    }

    public static bool IsGuardConflict(InvalidOperationException exception) =>
        exception.Message is
            "authentication_cutover_invalid_preparation"
            or "authentication_cutover_invalid_preparation_time"
            or "authentication_cutover_head_conflict"
            or "authentication_cutover_not_current"
            or "authentication_cutover_stale_stamp"
            or "authentication_cutover_instance_conflict"
            or "authentication_cutover_profile_conflict"
            or "authentication_cutover_not_prepared"
            or "authentication_cutover_wrong_target"
            or "authentication_cutover_conflicting_activation"
            or "authentication_cutover_unexpected_activation"
            or "authentication_cutover_conflicting_fence"
            or "authentication_cutover_incomplete_fence"
            or "authentication_cutover_not_collecting"
            or "authentication_cutover_terminal"
            or "authentication_cutover_late_callback"
            or "authentication_cutover_expired"
            or "authentication_cutover_head_invalid_preparation"
            or "authentication_cutover_head_invalid_replacement"
            or "authentication_cutover_not_ready"
            or "authentication_cutover_cleanup_targets_changed"
            or "authentication_credential_profile_unavailable"
            or "authentication_credential_profile_authority_invalid";

    public static async Task<AuthenticationCredentialCutoverDto> ReadCommittedReceiptAsync(
        AuthenticationCredentialCutover committed,
        IAuthenticationCredentialCutoverRepository repository,
        CancellationToken cancellationToken)
    {
        var operation = await repository.GetAsync(committed.InstanceId, committed.Id, cancellationToken)
            ?? throw new InvalidOperationException("authentication_cutover_receipt_unavailable");
        var head = await repository.GetHeadAsync(committed.InstanceId, cancellationToken);
        return AuthenticationCredentialCutoverReceiptMapper.Map(operation, head);
    }
}

public sealed class CleanupAuthenticationCredentialsCommandHandler(
    IUserContext userContext,
    IAdminContext adminContext,
    IInstanceBootstrapStateRepository bootstrapRepository,
    IAuthenticationCredentialCutoverCoordinator coordinator,
    IAuthenticationCredentialCutoverRepository repository,
    TimeProvider timeProvider)
    : ICommandHandler<CleanupAuthenticationCredentialsCommand, AuthenticationCredentialCutoverDto>
{
    public async Task<AuthenticationCredentialCutoverDto> ExecuteAsync(
        CleanupAuthenticationCredentialsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var (instanceId, _) = await AuthenticationCredentialCutoverHandlerSupport
                .RequireCurrentInstanceAsync(userContext, adminContext, bootstrapRepository, cancellationToken);
            if (request.Input is null)
                throw new ValidationException("Authentication credential cleanup approval is required.");
            new AuthenticationCredentialCleanupInputValidator().ValidateAndThrow(request.Input);
            var input = request.Input;
            var committed = await coordinator.CleanupAsync(instanceId, input.OperationId, input.DeploymentRevision,
                input.ExpectedHeadStamp, input.ExpectedOperationStamp,
                input.ApprovedSettingIds!, input.ApprovedBindingIds!, timeProvider.GetUtcNow(), cancellationToken);
            return await AuthenticationCredentialCutoverHandlerSupport.ReadCommittedReceiptAsync(
                committed, repository, cancellationToken);
        }
        catch (InvalidOperationException exception) when (
            AuthenticationCredentialCutoverHandlerSupport.IsGuardConflict(exception))
        {
            throw new ApplicationExceptions.ConcurrencyConflictException(
                exception.Message, "Authentication credential cleanup approval or authority changed.");
        }
    }
}

public sealed class PrepareAuthenticationCredentialCutoverCommandHandler(
    IUserContext userContext,
    IAdminContext adminContext,
    IInstanceBootstrapStateRepository bootstrapRepository,
    IAuthenticationCredentialProfileReader profileReader,
    IAuthenticationCredentialCutoverCoordinator coordinator,
    IAuthenticationCredentialCutoverRepository repository,
    TimeProvider timeProvider)
    : ICommandHandler<PrepareAuthenticationCredentialCutoverCommand, AuthenticationCredentialCutoverDto>
{
    public async Task<AuthenticationCredentialCutoverDto> ExecuteAsync(
        PrepareAuthenticationCredentialCutoverCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var (instanceId, actorId) = await AuthenticationCredentialCutoverHandlerSupport
                .RequireCurrentInstanceAsync(userContext, adminContext, bootstrapRepository, cancellationToken);
            if (request.Input is null)
                throw new ValidationException("Authentication credential cutover input is required.");
            new PrepareAuthenticationCredentialCutoverInputValidator().ValidateAndThrow(request.Input);
            var profile = profileReader.ReadCurrent();
            DateTimeOffset now = timeProvider.GetUtcNow();
            AuthenticationCredentialCutover operation;
            try
            {
                operation = AuthenticationCredentialCutover.Create(instanceId, Guid.CreateVersion7(), actorId,
                    now, now.AddMinutes(15), request.Input.DeploymentRevision, profile.Authority,
                    profile.Revision, request.Input.RequiredReplacements.Select(member =>
                        new AuthenticationCredentialCutover.Replacement(member.ReplicaId,
                            member.PreviousProcessBootId, member.ProcessBootId, member.Consumer,
                            member.Generation)));
            }
            catch (ArgumentException)
            {
                throw new ValidationException("Authentication credential cutover preparation is invalid.");
            }

            var committed = await coordinator.PrepareAsync(operation, request.Input.ExpectedCurrentOperationId,
                request.Input.ExpectedHeadStamp, request.Input.ExpectedPredecessorStamp, now, cancellationToken);
            return await AuthenticationCredentialCutoverHandlerSupport.ReadCommittedReceiptAsync(
                committed, repository, cancellationToken);
        }
        catch (InvalidOperationException exception) when (
            AuthenticationCredentialCutoverHandlerSupport.IsGuardConflict(exception))
        {
            throw new ApplicationExceptions.ConcurrencyConflictException(
                exception.Message, "Authentication credential cutover state or deployment profile changed.");
        }
    }
}

public sealed class ReportAuthenticationCredentialActivationCommandHandler(
    IUserContext userContext,
    IAdminContext adminContext,
    IInstanceBootstrapStateRepository bootstrapRepository,
    IAuthenticationCredentialCutoverCoordinator coordinator,
    IAuthenticationCredentialCutoverRepository repository,
    TimeProvider timeProvider)
    : ICommandHandler<ReportAuthenticationCredentialActivationCommand, AuthenticationCredentialCutoverDto>
{
    public async Task<AuthenticationCredentialCutoverDto> ExecuteAsync(
        ReportAuthenticationCredentialActivationCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var (instanceId, _) = await AuthenticationCredentialCutoverHandlerSupport
                .RequireCurrentInstanceAsync(userContext, adminContext, bootstrapRepository, cancellationToken);
            if (request.Input is null)
                throw new ValidationException("Authentication credential activation input is required.");
            new AuthenticationCredentialActivationInputValidator().ValidateAndThrow(request.Input);
            var input = request.Input;
            var report = new AuthenticationCredentialCutover.ActivationReport(instanceId, input.OperationId,
                input.DeploymentRevision, input.ReplicaId, input.ProcessBootId, input.Consumer, input.Generation);
            var committed = await coordinator.ReportActivationAsync(report, input.ExpectedHeadStamp,
                input.ExpectedOperationStamp, timeProvider.GetUtcNow(), cancellationToken);
            return await AuthenticationCredentialCutoverHandlerSupport.ReadCommittedReceiptAsync(
                committed, repository, cancellationToken);
        }
        catch (InvalidOperationException exception) when (
            AuthenticationCredentialCutoverHandlerSupport.IsGuardConflict(exception))
        {
            throw new ApplicationExceptions.ConcurrencyConflictException(
                exception.Message, "Authentication credential cutover state or deployment profile changed.");
        }
    }
}

public sealed class AttestAuthenticationCredentialFenceCommandHandler(
    IUserContext userContext,
    IAdminContext adminContext,
    IInstanceBootstrapStateRepository bootstrapRepository,
    IAuthenticationCredentialCutoverCoordinator coordinator,
    IAuthenticationCredentialCutoverRepository repository,
    TimeProvider timeProvider)
    : ICommandHandler<AttestAuthenticationCredentialFenceCommand, AuthenticationCredentialCutoverDto>
{
    public async Task<AuthenticationCredentialCutoverDto> ExecuteAsync(
        AttestAuthenticationCredentialFenceCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var (instanceId, actorId) = await AuthenticationCredentialCutoverHandlerSupport
                .RequireCurrentInstanceAsync(userContext, adminContext, bootstrapRepository, cancellationToken);
            if (request.Input is null)
                throw new ValidationException("Authentication credential fence input is required.");
            new AuthenticationCredentialFenceInputValidator().ValidateAndThrow(request.Input);
            var input = request.Input;
            var fence = new AuthenticationCredentialCutover.FenceAttestation(instanceId, input.OperationId,
                input.DeploymentRevision, actorId, input.OldProcessesTerminated,
                input.OldWritersFenced, input.OldSessionsFenced);
            var committed = await coordinator.AttestFenceAsync(fence, input.ExpectedHeadStamp,
                input.ExpectedOperationStamp, timeProvider.GetUtcNow(), cancellationToken);
            return await AuthenticationCredentialCutoverHandlerSupport.ReadCommittedReceiptAsync(
                committed, repository, cancellationToken);
        }
        catch (InvalidOperationException exception) when (
            AuthenticationCredentialCutoverHandlerSupport.IsGuardConflict(exception))
        {
            throw new ApplicationExceptions.ConcurrencyConflictException(
                exception.Message, "Authentication credential cutover state or deployment profile changed.");
        }
    }
}
