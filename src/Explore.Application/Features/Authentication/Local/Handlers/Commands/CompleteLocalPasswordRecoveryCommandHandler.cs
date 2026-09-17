
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Validators;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Responses;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

public sealed class CompleteLocalPasswordRecoveryCommandHandler(
    ILocalIdentityLifecycleStore lifecycle,
    ICommandHandler<SyncUserCommand, BaseCommandResponse<Guid>> syncUserCommandHandler,
    HybridCache cache)
    : ICommandHandler<CompleteLocalPasswordRecoveryCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CompleteLocalPasswordRecoveryCommand request, CancellationToken cancellationToken = default)
    {
        var body = request.Request;
        var validation = await new LocalPasswordRecoveryCompletionRequestDtoValidator().ValidateAsync(body, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(errors: ["A complete bounded recovery operation and password are required."]);
        var operation = new LocalIdentityLifecyclePointer(body.OperationId, body.LocalSubjectId,
            body.PersonalActorId, body.ExternalLoginId, body.Purpose, body.Generation);
        return await LocalIdentityLifecycleConsumptionOrchestrator.ExecuteAsync(
            new LocalIdentityLifecycleConsumption(operation, body.Token, body.NewPassword), lifecycle, syncUserCommandHandler, cache, cancellationToken);
    }
}
