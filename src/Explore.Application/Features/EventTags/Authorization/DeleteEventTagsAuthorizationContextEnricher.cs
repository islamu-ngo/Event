using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventTags.Requests.Commands;

namespace Explore.Application.Features.EventTags.Authorization;

public sealed class DeleteEventTagsAuthorizationContextEnricher(
    IEventTagsRepository repository)
    : IAuthorizationContextEnricher<DeleteEventTagsCommand>
{
    public async Task<AuthorizationContext> ResolveAsync(
        DeleteEventTagsCommand request,
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
