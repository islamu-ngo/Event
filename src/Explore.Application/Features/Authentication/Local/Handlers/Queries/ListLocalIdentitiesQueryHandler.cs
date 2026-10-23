
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Exceptions;
using Explore.Application.Features.Authentication.Local.Requests.Queries;
using FluentValidation.Results;

namespace Explore.Application.Features.Authentication.Local.Handlers.Queries;

public sealed class ListLocalIdentitiesQueryHandler(
    IAdminContext adminContext,
    ILocalHumanCredentialAdministration credentialAdministration,
    ILocalAdministrationPreparation preparation) : IQueryHandler<ListLocalIdentitiesQuery, LocalIdentityPage>
{
    public async Task<LocalIdentityPage> QueryAsync(ListLocalIdentitiesQuery request, CancellationToken cancellationToken = default)
    {
        var original = await LocalCredentialAdministrator.ResolveAsync(
            adminContext, preparation, request, cancellationToken)
            .ConfigureAwait(false);
        if (original is null)
        {
            throw new AuthorizationException(message: "Instance credential administration is required.");
        }
        try
        {
            _ = new LocalIdentityListRequest(pageNumber: request.PageNumber, pageSize: request.PageSize);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or OverflowException)
        {
            throw new ValidationException(new ValidationResult(
                [new ValidationFailure(propertyName: nameof(request.PageNumber), errorMessage: "Pagination is outside its permitted bounds.")]));
        }
        return await credentialAdministration.ListAsync(original, request, cancellationToken).ConfigureAwait(false);
    }
}
