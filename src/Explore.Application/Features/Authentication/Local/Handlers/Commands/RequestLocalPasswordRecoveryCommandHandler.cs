
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Validators;
using Explore.Application.Responses;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

public sealed class RequestLocalPasswordRecoveryCommandHandler(
    ILocalIdentityLifecycleStore lifecycle,
    IAccountAuthorityLifecycleEmailService email)
    : ICommandHandler<RequestLocalPasswordRecoveryCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(RequestLocalPasswordRecoveryCommand request, CancellationToken cancellationToken = default)
    {
        var validation = await new LocalPasswordRecoveryRequestDtoValidator().ValidateAsync(request.Request, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(errors: ["A bounded Local account identifier is required."]);

        LocalIdentityLifecycleRequest? target;
        try
        {
            target = await lifecycle.FindRequestByIdentifierAsync(
                request.Request.Identifier, LocalIdentityLifecyclePurpose.PasswordRecovery, cancellationToken);
        }
        catch (Exception exception) when (LocalIdentityIntakeFailureClassifier.IsTransientStoreFailure(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return BaseCommandResponse.Success(id: Guid.Empty);
        }
        if (target is not null)
        {
            // Classified pre-commit failures and ineligibility share the empty public intake response.
            // Only recorded operations belong to durable delivery; uncommitted work has no retry promise.
            await email.RequestPasswordResetAsync(new AccountAuthorityLifecycleEmailRequest(
                UserId: target.LocalSubjectId, ExternalLoginId: target.ExternalLoginId), cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return BaseCommandResponse.Success(id: Guid.Empty);
    }
}
