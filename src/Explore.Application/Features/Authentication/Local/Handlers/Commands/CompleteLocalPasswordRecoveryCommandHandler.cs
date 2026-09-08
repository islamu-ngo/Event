// ABOUTME: Validates recovery-only Local token and password input before native one-use mutation.
// ABOUTME: Keeps token-authorized mirror retry separate from password mutation and ordinary login.

using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Authentication.Local.Validators;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

public sealed class CompleteLocalPasswordRecoveryCommandHandler(ILocalIdentityLifecycleStore lifecycle, ISender sender, HybridCache cache)
    : IRequestHandler<CompleteLocalPasswordRecoveryCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(CompleteLocalPasswordRecoveryCommand request, CancellationToken cancellationToken)
    {
        var body = request.Request;
        var validation = await new LocalPasswordRecoveryCompletionRequestDtoValidator().ValidateAsync(body, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(errors: ["A complete bounded recovery operation and password are required."]);
        var operation = new LocalIdentityLifecyclePointer(body.OperationId, body.LocalSubjectId,
            body.PersonalActorId, body.ExternalLoginId, body.Purpose, body.Generation);
        return await LocalIdentityLifecycleConsumptionOrchestrator.ExecuteAsync(
            new LocalIdentityLifecycleConsumption(operation, body.Token, body.NewPassword), lifecycle, sender, cache, cancellationToken);
    }
}
