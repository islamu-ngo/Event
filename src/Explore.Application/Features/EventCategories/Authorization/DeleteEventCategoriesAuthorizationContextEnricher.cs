using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventCategories.Requests.Commands;

namespace Explore.Application.Features.EventCategories.Authorization;

public sealed class DeleteEventCategoriesAuthorizationContextEnricher(
    IEventCategoriesRepository repository)
    : IAuthorizationContextEnricher<DeleteEventCategoriesCommand>
{
    public async Task<AuthorizationContext> ResolveAsync(
        DeleteEventCategoriesCommand request,
        CancellationToken cancellationToken)
    {
        var assignment = await repository.GetById(request.Id);
        if (assignment is null)
        {
            throw new AuthorizationException(ResourceKinds.Event, AuthorizationActions.Update);
        }

        return new AuthorizationContext(assignment.EventId.ToString(), null);
    }
}
