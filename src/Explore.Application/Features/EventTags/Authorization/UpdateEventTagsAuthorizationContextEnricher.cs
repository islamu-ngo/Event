using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventTags.Requests.Commands;

namespace Explore.Application.Features.EventTags.Authorization;

public sealed class UpdateEventTagsAuthorizationContextEnricher(
    IEventTagsRepository repository)
    : IAuthorizationContextEnricher<UpdateEventTagsCommand>
{
    public async Task<AuthorizationContext> ResolveAsync(
        UpdateEventTagsCommand request,
        CancellationToken cancellationToken)
    {
        var assignment = await repository.GetById(request.EventTagId);
        if (assignment is null)
        {
            throw new AuthorizationException(ResourceKinds.Event, AuthorizationActions.Update);
        }

        return new AuthorizationContext(assignment.EventId.ToString(), null);
    }
}
