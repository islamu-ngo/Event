using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventCategories.Requests.Commands;

namespace Explore.Application.Features.EventCategories.Authorization;

public sealed class UpdateEventCategoriesAuthorizationContextEnricher(
    IEventCategoriesRepository repository)
    : IAuthorizationContextEnricher<UpdateEventCategoriesCommand>
{
    public async Task<AuthorizationContext> ResolveAsync(
        UpdateEventCategoriesCommand request,
        CancellationToken cancellationToken)
    {
        var assignment = await repository.GetById(request.EventCategoryId);
        if (assignment is null)
        {
            throw new AuthorizationException(ResourceKinds.Event, AuthorizationActions.Update);
        }

        return new AuthorizationContext(assignment.EventId.ToString(), null);
    }
}
