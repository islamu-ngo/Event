
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Authentication.Local.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Authentication.Local.Handlers.Queries;

public sealed class GetLocalCredentialOperationQueryHandler(
    IAdminContext adminContext,
    IPlatformUserRoleRepository platformUserRoles,
    ILocalCredentialAdministration credentialAdministration)
    : IRequestHandler<GetLocalCredentialOperationQuery, LocalCredentialOperationStatus?>
{
    public async Task<LocalCredentialOperationStatus?> Handle(
        GetLocalCredentialOperationQuery request, CancellationToken cancellationToken)
    {
        Guid? actor = await LocalCredentialAdministrator.ResolveAsync(
            adminContext: adminContext, platformUserRoles: platformUserRoles, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (actor is null)
        {
            throw new AuthorizationException(message: "Instance credential administration is required.");
        }
        if (request.OperationId == Guid.Empty)
        {
            return null;
        }
        LocalCredentialOperationStatus? status = await credentialAdministration.ReadOperationAsync(
            operationId: request.OperationId, cancellationToken: cancellationToken).ConfigureAwait(false);
        Guid? current = await LocalCredentialAdministrator.ResolveAsync(
            adminContext: adminContext, platformUserRoles: platformUserRoles, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (current != actor)
        {
            throw new AuthorizationException(message: "Instance credential administration is required.");
        }
        return status;
    }
}
