// ABOUTME: Returns bounded Local identity administration pages only to current instance administrators.
// ABOUTME: Validates pagination before native reads and rechecks authority before disclosing identity summaries.

using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Authentication.Local.Requests.Queries;
using FluentValidation.Results;
using MediatR;

namespace Explore.Application.Features.Authentication.Local.Handlers.Queries;

public sealed class ListLocalIdentitiesQueryHandler(
    IAdminContext adminContext,
    IPlatformUserRoleRepository platformUserRoles,
    ILocalCredentialAdministration credentialAdministration) : IRequestHandler<ListLocalIdentitiesQuery, LocalIdentityPage>
{
    public async Task<LocalIdentityPage> Handle(ListLocalIdentitiesQuery request, CancellationToken cancellationToken)
    {
        Guid? actor = await LocalCredentialAdministrator.ResolveAsync(
            adminContext: adminContext, platformUserRoles: platformUserRoles, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (actor is null)
        {
            throw new AuthorizationException(message: "Instance credential administration is required.");
        }
        LocalIdentityListRequest read;
        try
        {
            read = new LocalIdentityListRequest(pageNumber: request.PageNumber, pageSize: request.PageSize);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or OverflowException)
        {
            throw new ValidationException(new ValidationResult(
                [new ValidationFailure(propertyName: nameof(request.PageNumber), errorMessage: "Pagination is outside its permitted bounds.")]));
        }
        LocalIdentityPage page = await credentialAdministration.ListAsync(read, cancellationToken).ConfigureAwait(false);
        Guid? current = await LocalCredentialAdministrator.ResolveAsync(
            adminContext: adminContext, platformUserRoles: platformUserRoles, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (current != actor)
        {
            throw new AuthorizationException(message: "Instance credential administration is required.");
        }
        return page;
    }
}
