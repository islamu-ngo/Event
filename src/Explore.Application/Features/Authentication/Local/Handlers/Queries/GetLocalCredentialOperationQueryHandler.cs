
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Exceptions;
using Explore.Application.Features.Authentication.Local.Requests.Queries;

namespace Explore.Application.Features.Authentication.Local.Handlers.Queries;

public sealed class GetLocalCredentialOperationQueryHandler(
    IAdminContext adminContext,
    ILocalHumanCredentialAdministration credentialAdministration,
    ILocalAdministrationPreparation preparation)
    : IQueryHandler<GetLocalCredentialOperationQuery, LocalCredentialOperationStatus?>
{
    public async Task<LocalCredentialOperationStatus?> QueryAsync(
        GetLocalCredentialOperationQuery request, CancellationToken cancellationToken = default)
    {
        var original = await LocalCredentialAdministrator.ResolveAsync(
            adminContext, preparation, request, cancellationToken)
            .ConfigureAwait(false);
        if (original is null)
        {
            throw new AuthorizationException(message: "Instance credential administration is required.");
        }
        if (request.OperationId == Guid.Empty)
        {
            return null;
        }
        return await credentialAdministration.ReadOperationAsync(original, request, cancellationToken).ConfigureAwait(false);
    }
}
