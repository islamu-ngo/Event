using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessionLanguages.Requests.Commands;

namespace Explore.Application.Features.EventSessionLanguages.Authorization;

public sealed class UpdateEventSessionLanguageAuthorizationContextEnricher(
    IEventSessionLanguageRepository repository)
    : IAuthorizationContextEnricher<UpdateEventSessionLanguageCommand>
{
    public async Task<AuthorizationContext> ResolveAsync(
        UpdateEventSessionLanguageCommand request,
        CancellationToken cancellationToken)
    {
        var assignment = await repository.GetById(request.EventSessionLanguageId);
        if (assignment is null)
        {
            throw new AuthorizationException(ResourceKinds.EventSession, AuthorizationActions.Update);
        }

        return new AuthorizationContext(assignment.EventSessionId.ToString(), null);
    }
}
