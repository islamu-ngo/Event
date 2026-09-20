
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

        LocalIdentityLifecycleRequest? target = await lifecycle.FindRequestByIdentifierAsync(
            request.Request.Identifier, LocalIdentityLifecyclePurpose.PasswordRecovery, cancellationToken);
        if (target is not null)
        {
            // Even provider/configuration failures are hidden here: a target-specific call cannot establish
            // that an outage is global. Delivery remains durable and independent of this public response.
            await email.RequestPasswordResetAsync(new AccountAuthorityLifecycleEmailRequest(
                UserId: target.LocalSubjectId, ExternalLoginId: target.ExternalLoginId), cancellationToken);
        }
        return BaseCommandResponse.Success(id: Guid.Empty);
    }
}
