// ABOUTME: Separates anonymous current-address verification from authenticated Local proposed-address authority.
// ABOUTME: Routes only exact native Local bindings and never accepts another target beside a current session.

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Authentication.Local.Validators;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

public sealed class RequestLocalEmailVerificationCommandHandler(
    ILocalIdentityLifecycleStore lifecycle,
    ILocalCredentialAdministration credentials,
    ILocalIdentityAuthService authentication,
    IAccountAuthorityLifecycleEmailService email)
    : IRequestHandler<RequestLocalEmailVerificationCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(RequestLocalEmailVerificationCommand request, CancellationToken cancellationToken)
    {
        var validation = await new LocalEmailVerificationRequestDtoValidator().ValidateAsync(request.Request, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(errors: ["A bounded verification request is required."]);

        if (request.Authority is { } authority)
        {
            if (request.Request.Identifier is not null)
                return BaseCommandResponse.Validation<Guid>(errors: ["A signed-in request cannot select another account."]);
            LocalSessionValidationOutcome session = await authentication.ValidateSessionAsync(authority, cancellationToken);
            if (session == LocalSessionValidationOutcome.Unavailable)
                return BaseCommandResponse.Failure<Guid>("local_lifecycle_unavailable", "Local identity validation is unavailable.");
            if (session != LocalSessionValidationOutcome.Valid)
                return BaseCommandResponse.Authentication<Guid>();
            LocalIdentityBinding? binding = await credentials.ReadLinkedIdentityAsync(authority.LocalSubjectId, cancellationToken);
            if (binding?.CredentialState != LocalCredentialState.Ready)
                return BaseCommandResponse.Authentication<Guid>();

            var delivery = new AccountAuthorityLifecycleEmailRequest(
                UserId: binding.LocalSubjectId, ExternalLoginId: binding.ExternalLoginId,
                ProposedEmail: request.Request.ProposedEmail, ExpectedSecurityStamp: authority.SecurityStamp);
            AccountAuthorityLifecycleEmailResult result = request.Request.ProposedEmail is null
                ? await email.RequestEmailVerificationAsync(delivery, cancellationToken)
                : await email.RequestEmailUpdateVerificationAsync(delivery, cancellationToken);
            return result.Status switch
            {
                AccountAuthorityLifecycleEmailStatus.DelegationRecorded => BaseCommandResponse.Success(id: Guid.Empty),
                AccountAuthorityLifecycleEmailStatus.Disabled or AccountAuthorityLifecycleEmailStatus.ProviderNotConfigured =>
                    BaseCommandResponse.Failure<Guid>("local_lifecycle_unavailable", "Local email delivery is unavailable."),
                _ => BaseCommandResponse.Validation<Guid>(errors: ["The verification request could not be accepted."])
            };
        }

        if (request.Request.ProposedEmail is not null)
            return BaseCommandResponse.Authentication<Guid>();
        if (string.IsNullOrWhiteSpace(request.Request.Identifier))
            return BaseCommandResponse.Validation<Guid>(errors: ["A Local account identifier is required."]);
        LocalIdentityLifecycleRequest? target = await lifecycle.FindRequestByIdentifierAsync(
            request.Request.Identifier, LocalIdentityLifecyclePurpose.EmailVerification, cancellationToken);
        if (target is not null)
            await email.RequestEmailVerificationAsync(new AccountAuthorityLifecycleEmailRequest(
                UserId: target.LocalSubjectId, ExternalLoginId: target.ExternalLoginId), cancellationToken);
        return BaseCommandResponse.Success(id: Guid.Empty);
    }
}
