
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Validators;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Responses;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

public sealed class ConfirmLocalEmailCommandHandler(
    ILocalIdentityLifecycleStore lifecycle,
    ICommandHandler<SyncUserCommand, BaseCommandResponse<Guid>> syncUserCommandHandler,
    HybridCache cache)
    : ICommandHandler<ConfirmLocalEmailCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(ConfirmLocalEmailCommand request, CancellationToken cancellationToken = default)
    {
        var body = request.Request;
        var validation = await new LocalEmailConfirmationRequestDtoValidator().ValidateAsync(body, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(errors: ["A complete bounded email operation is required."]);
        var operation = new LocalIdentityLifecyclePointer(body.OperationId, body.LocalSubjectId,
            body.PersonalActorId, body.ExternalLoginId, body.Purpose, body.Generation);
        return await LocalIdentityLifecycleConsumptionOrchestrator.ExecuteAsync(
            new LocalIdentityLifecycleConsumption(operation, body.Token), lifecycle, syncUserCommandHandler, cache, cancellationToken);
    }
}
