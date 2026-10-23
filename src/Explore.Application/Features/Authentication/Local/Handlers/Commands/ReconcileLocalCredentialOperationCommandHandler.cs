using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Responses;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

public sealed class ReconcileLocalCredentialOperationCommandHandler(
    IAdminContext adminContext,
    ILocalHumanCredentialAdministration credentialAdministration,
    ILocalAdministrationPreparation preparation)
    : ICommandHandler<ReconcileLocalCredentialOperationCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ReconcileLocalCredentialOperationCommand request, CancellationToken cancellationToken = default)
    {
        var original = await LocalCredentialAdministrator.ResolveAsync(adminContext, preparation, request, cancellationToken);
        if (original is null) return BaseCommandResponse.Authorization<Guid>();
        if (request.OperationId == Guid.Empty)
            return BaseCommandResponse.Validation<Guid>(errors: ["An operation identifier is required."]);
        return await LocalCredentialAdministrator.ReconcileAsync(credentialAdministration, original, request,
            request.OperationId, cancellationToken);
    }
}
