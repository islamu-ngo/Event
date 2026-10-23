
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Validators;
using Explore.Application.Responses;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

public sealed class CreateLocalIdentityCommandHandler(
    IAdminContext adminContext,
    ILocalHumanCredentialAdministration credentialAdministration,
    ILocalAdministrationPreparation preparation)
    : ICommandHandler<CreateLocalIdentityCommand, LocalCredentialIssueCommandResponse>
{
    public async Task<LocalCredentialIssueCommandResponse> ExecuteAsync(
        CreateLocalIdentityCommand request, CancellationToken cancellationToken = default)
    {
        var original = await LocalCredentialAdministrator.ResolveAsync(
            adminContext, preparation, request, cancellationToken)
            .ConfigureAwait(false);
        if (original is null)
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
        LocalCredentialCreateResult creation = await credentialAdministration.CreatePendingAsync(original, request, cancellationToken)
            .ConfigureAwait(false);
        switch (creation.Outcome)
        {
            case LocalCredentialCreateOutcome.Invalid:
                return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Validation<Guid>(
                    errors: ["Local identity creation was rejected."], id: request.OperationId));
            case LocalCredentialCreateOutcome.Conflict:
                return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Conflict(id: request.OperationId));
            case LocalCredentialCreateOutcome.Created:
                BaseCommandResponse<Guid> reconciliation = await LocalCredentialAdministrator.ReconcileAsync(
                    credentialAdministration, original, request, request.OperationId,
                    cancellationToken);
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
            original, request, cancellationToken).ConfigureAwait(false);
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
