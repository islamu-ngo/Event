
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Validators;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

public sealed class ResetLocalCredentialCommandHandler(
    IAdminContext adminContext,
    IPlatformUserRoleRepository platformUserRoles,
    ILocalCredentialAdministration credentialAdministration)
    : IRequestHandler<ResetLocalCredentialCommand, LocalCredentialIssueCommandResponse>
{
    public async Task<LocalCredentialIssueCommandResponse> Handle(
        ResetLocalCredentialCommand request, CancellationToken cancellationToken)
    {
        Guid? actor = await LocalCredentialAdministrator.ResolveAsync(
            adminContext: adminContext, platformUserRoles: platformUserRoles, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (actor is null)
        {
            return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Authorization<Guid>());
        }
        var validation = await new ResetLocalCredentialRequestDtoValidator().ValidateAsync(
            new ResetLocalCredentialRequestDto
            {
                OperationId = request.OperationId, ExpectedCurrentOperationId = request.ExpectedCurrentOperationId,
                ExpectedCurrentOperationConcurrencyStamp = request.ExpectedCurrentOperationConcurrencyStamp,
                Reason = request.Reason
            }, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid || request.LocalSubjectId == Guid.Empty)
        {
            return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Validation<Guid>(
                errors: validation.IsValid ? ["A Local subject is required."]
                    : validation.Errors.Select(error => error.ErrorMessage)));
        }
        var intent = new LocalCredentialResetRequest(
            operationId: request.OperationId, initiatingApplicationUserId: actor.Value,
            localSubjectId: request.LocalSubjectId, expectedCurrentOperationId: request.ExpectedCurrentOperationId,
            expectedCurrentOperationConcurrencyStamp: request.ExpectedCurrentOperationConcurrencyStamp,
            reason: request.Reason);
        if (await LocalCredentialAdministrator.ResolveAsync(
                adminContext: adminContext, platformUserRoles: platformUserRoles, cancellationToken: cancellationToken)
                .ConfigureAwait(false) != actor)
        {
            return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Authorization<Guid>());
        }
        LocalCredentialResetResult reset = await credentialAdministration.ResetAsync(intent, cancellationToken)
            .ConfigureAwait(false);
        switch (reset.Outcome)
        {
            case LocalCredentialResetOutcome.NotFound:
                return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.NotFound<Guid>(id: request.OperationId));
            case LocalCredentialResetOutcome.Conflict:
            case LocalCredentialResetOutcome.BindingIncomplete:
                return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Conflict(id: request.OperationId));
            case LocalCredentialResetOutcome.Invalid:
                return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Validation<Guid>(
                    errors: ["Local credential reset was rejected."], id: request.OperationId));
            case LocalCredentialResetOutcome.Reset:
            case LocalCredentialResetOutcome.Replayed:
                break;
            default:
                throw new InvalidOperationException("Unknown Local reset outcome.");
        }
        LocalCredentialOperationStatus? status = await credentialAdministration.ReadOperationAsync(
            operationId: request.OperationId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (await LocalCredentialAdministrator.ResolveAsync(
                adminContext: adminContext, platformUserRoles: platformUserRoles, cancellationToken: cancellationToken)
                .ConfigureAwait(false) != actor)
        {
            return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Authorization<Guid>());
        }
        if (status is null)
        {
            return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Conflict(id: request.OperationId));
        }
        if (reset.Outcome == LocalCredentialResetOutcome.Replayed)
        {
            return LocalCredentialIssueCommandResponse.Replayed(LocalCredentialIssueDto.Replayed(operation: status));
        }
        if (!status.IsCurrent || status.CredentialState != LocalCredentialState.ChangeRequired
            || status.Receipt.Stage != LocalCredentialOperationStage.ChangeRequired)
        {
            return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Conflict(id: request.OperationId));
        }
        return LocalCredentialIssueCommandResponse.Issued(LocalCredentialIssueDto.Issued(
            operation: status, temporaryPassword: reset.TemporaryPassword!));
    }
}
