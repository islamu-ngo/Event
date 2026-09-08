// ABOUTME: Changes an ordinary Local password using a current session and the exact persisted Ready binding.
// ABOUTME: Does not use SMTP, supervised replacement authority, or authentication response generation.

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Authentication.Local.Validators;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

public sealed class ChangeLocalPasswordCommandHandler(
    ILocalIdentityLifecycleStore lifecycle,
    ILocalCredentialAdministration credentials,
    ILocalIdentityAuthService authentication)
    : IRequestHandler<ChangeLocalPasswordCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(ChangeLocalPasswordCommand request, CancellationToken cancellationToken)
    {
        if (request.Authority is not { } authority)
            return BaseCommandResponse.Authentication<Guid>();
        var validation = await new LocalPasswordChangeRequestDtoValidator().ValidateAsync(request.Request, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(errors: ["A bounded current and new password are required."]);
        LocalSessionValidationOutcome session = await authentication.ValidateSessionAsync(authority, cancellationToken);
        if (session == LocalSessionValidationOutcome.Unavailable)
            return BaseCommandResponse.Failure<Guid>("local_lifecycle_unavailable", "Local identity validation is unavailable.");
        if (session != LocalSessionValidationOutcome.Valid)
            return BaseCommandResponse.Authentication<Guid>();
        LocalIdentityBinding? binding = await credentials.ReadLinkedIdentityAsync(authority.LocalSubjectId, cancellationToken);
        if (binding?.CredentialState != LocalCredentialState.Ready)
            return BaseCommandResponse.Authentication<Guid>();
        LocalIdentityLifecycleOutcome outcome = await lifecycle.ChangePasswordAsync(new LocalIdentityPasswordChangeRequest(
            authority, binding.PersonalActorId, binding.ExternalLoginId,
            request.Request.CurrentPassword, request.Request.NewPassword), cancellationToken);
        return outcome switch
        {
            LocalIdentityLifecycleOutcome.Consumed => BaseCommandResponse.Success(id: binding.LocalSubjectId),
            LocalIdentityLifecycleOutcome.Conflict => BaseCommandResponse.Conflict(id: binding.LocalSubjectId),
            _ => BaseCommandResponse.Validation<Guid>(errors: ["The password change could not be accepted."])
        };
    }
}
