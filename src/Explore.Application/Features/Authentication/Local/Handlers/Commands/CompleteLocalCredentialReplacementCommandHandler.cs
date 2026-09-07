// ABOUTME: Defines the fail-closed command boundary for purpose-authorized Local password replacement.
// ABOUTME: Returns no ordinary session or user synchronization result from a replacement operation.

using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Contracts.Identity;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

public sealed class CompleteLocalCredentialReplacementCommandHandler(ILocalCredentialAdministration credentialAdministration)
    : IRequestHandler<CompleteLocalCredentialReplacementCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        CompleteLocalCredentialReplacementCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        LocalCredentialReplacementOutcome outcome = await credentialAdministration
            .ReplaceAsync(request.Request, cancellationToken).ConfigureAwait(false);
        Guid operationId = request.Request.Authority.Subject.OperationId;
        return outcome switch
        {
            LocalCredentialReplacementOutcome.Replaced => BaseCommandResponse.Success(id: operationId),
            LocalCredentialReplacementOutcome.InvalidChallenge => BaseCommandResponse.Authentication<Guid>(
                message: "A current replacement challenge is required."),
            LocalCredentialReplacementOutcome.SamePassword => BaseCommandResponse.Validation<Guid>(
                errors: ["Choose a password different from the temporary password."],
                message: "The password must be replaced."),
            LocalCredentialReplacementOutcome.InvalidPassword => BaseCommandResponse.Validation<Guid>(
                errors: ["The proposed password does not meet the password requirements."],
                message: "The proposed password could not be accepted."),
            LocalCredentialReplacementOutcome.Conflict => BaseCommandResponse.Conflict(id: operationId,
                message: "The credential operation changed."),
            _ => throw new InvalidOperationException("Unknown credential replacement outcome.")
        };
    }
}
