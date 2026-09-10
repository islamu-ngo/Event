
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Validators;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

public sealed class CreateLocalIdentityCommandHandler(
    IAdminContext adminContext,
    IPlatformUserRoleRepository platformUserRoles,
    ILocalCredentialAdministration credentialAdministration,
    ISender sender) : IRequestHandler<CreateLocalIdentityCommand, LocalCredentialIssueCommandResponse>
{
    public async Task<LocalCredentialIssueCommandResponse> Handle(
        CreateLocalIdentityCommand request, CancellationToken cancellationToken)
    {
        Guid? actor = await LocalCredentialAdministrator.ResolveAsync(
            adminContext: adminContext, platformUserRoles: platformUserRoles, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (actor is null)
        {
            return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Authorization<Guid>());
        }
        var validation = await new CreateLocalIdentityRequestDtoValidator().ValidateAsync(
            new CreateLocalIdentityRequestDto
            {
                OperationId = request.OperationId,
                Email = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName
            }, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Validation<Guid>(
                errors: validation.Errors.Select(error => error.ErrorMessage)));
        }
        var intent = new LocalCredentialCreateRequest(
            operationId: request.OperationId, initiatingApplicationUserId: actor.Value,
            email: request.Email, firstName: request.FirstName, lastName: request.LastName);
        if (await LocalCredentialAdministrator.ResolveAsync(
                adminContext: adminContext, platformUserRoles: platformUserRoles, cancellationToken: cancellationToken)
                .ConfigureAwait(false) != actor)
        {
            return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Authorization<Guid>());
        }
        LocalCredentialCreateResult creation = await credentialAdministration.CreatePendingAsync(intent, cancellationToken)
            .ConfigureAwait(false);
        switch (creation.Outcome)
        {
            case LocalCredentialCreateOutcome.Invalid:
                return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Validation<Guid>(
                    errors: ["Local identity creation was rejected."], id: request.OperationId));
            case LocalCredentialCreateOutcome.Conflict:
                return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Conflict(id: request.OperationId));
            case LocalCredentialCreateOutcome.Created:
                if (await LocalCredentialAdministrator.ResolveAsync(
                        adminContext: adminContext, platformUserRoles: platformUserRoles, cancellationToken: cancellationToken)
                        .ConfigureAwait(false) != actor)
                {
                    return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Authorization<Guid>());
                }
                BaseCommandResponse<Guid> reconciliation = await sender.Send(
                    new ReconcileLocalCredentialOperationCommand(operationId: request.OperationId), cancellationToken)
                    .ConfigureAwait(false);
                if (!reconciliation.IsSuccess)
                {
                    return LocalCredentialIssueCommandResponse.Failure(reconciliation);
                }
                break;
            case LocalCredentialCreateOutcome.Replayed:
                break;
            default:
                throw new InvalidOperationException("Unknown Local creation outcome.");
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
        if (creation.Outcome == LocalCredentialCreateOutcome.Replayed)
        {
            return LocalCredentialIssueCommandResponse.Replayed(LocalCredentialIssueDto.Replayed(operation: status));
        }
        if (!status.IsCurrent || status.CredentialState != LocalCredentialState.ChangeRequired
            || status.Receipt.Stage != LocalCredentialOperationStage.ChangeRequired)
        {
            return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Conflict(id: request.OperationId));
        }
        return LocalCredentialIssueCommandResponse.Issued(LocalCredentialIssueDto.Issued(
            operation: status, temporaryPassword: creation.TemporaryPassword!));
    }
}
