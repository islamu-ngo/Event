// ABOUTME: Validates purpose-bound Local email consumption before entering native operation authority.
// ABOUTME: Reuses serialized SyncUser mirror orchestration without fabricating a login response.

using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Validators;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

public sealed class ConfirmLocalEmailCommandHandler(ILocalIdentityLifecycleStore lifecycle, ISender sender, HybridCache cache)
    : IRequestHandler<ConfirmLocalEmailCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(ConfirmLocalEmailCommand request, CancellationToken cancellationToken)
    {
        var body = request.Request;
        var validation = await new LocalEmailConfirmationRequestDtoValidator().ValidateAsync(body, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(errors: ["A complete bounded email operation is required."]);
        var operation = new LocalIdentityLifecyclePointer(body.OperationId, body.LocalSubjectId,
            body.PersonalActorId, body.ExternalLoginId, body.Purpose, body.Generation);
        return await LocalIdentityLifecycleConsumptionOrchestrator.ExecuteAsync(
            new LocalIdentityLifecycleConsumption(operation, body.Token), lifecycle, sender, cache, cancellationToken);
    }
}
