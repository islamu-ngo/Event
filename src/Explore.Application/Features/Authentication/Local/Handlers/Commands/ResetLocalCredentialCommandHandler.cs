
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Validators;
using Explore.Application.Responses;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

public sealed class ResetLocalCredentialCommandHandler(
    IAdminContext adminContext,
    ILocalHumanCredentialAdministration credentialAdministration,
    ILocalAdministrationPreparation preparation)
    : ICommandHandler<ResetLocalCredentialCommand, LocalCredentialIssueCommandResponse>
{
    public async Task<LocalCredentialIssueCommandResponse> ExecuteAsync(
        ResetLocalCredentialCommand request, CancellationToken cancellationToken = default)
    {
        var original = await LocalCredentialAdministrator.ResolveAsync(
            adminContext, preparation, request, cancellationToken)
            .ConfigureAwait(false);
        if (original is null)
        {
            return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Authorization<Guid>());
        }
        var validation = await new ResetLocalCredentialRequestDtoValidator().ValidateAsync(
            new ResetLocalCredentialRequestDto
            {
                OperationId = request.OperationId,
                ExpectedCurrentOperationId = request.ExpectedCurrentOperationId,
                ExpectedCurrentOperationConcurrencyStamp = request.ExpectedCurrentOperationConcurrencyStamp,
                Reason = request.Reason
            }, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid || request.LocalSubjectId == Guid.Empty)
        {
            return LocalCredentialIssueCommandResponse.Failure(BaseCommandResponse.Validation<Guid>(
                errors: validation.IsValid ? ["A Local subject is required."]
                    : validation.Errors.Select(error => error.ErrorMessage)));
        }
        LocalCredentialResetResult reset = await credentialAdministration.ResetAsync(original, request, cancellationToken)
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
            original, request, cancellationToken).ConfigureAwait(false);
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
