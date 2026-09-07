namespace Explore.Application.Authorization;

public interface IAuthorizationContextEnricher<in TRequest>
    where TRequest : notnull
{
    Task<AuthorizationContext> ResolveAsync(TRequest request, CancellationToken cancellationToken);
}
