using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Behaviors;

/// <summary>Integrates the shared authoritative evaluator with the remaining MediatR cohort.</summary>
public sealed class AuthorizationBehavior<TRequest, TResponse>(
    IAuthorizationProvider authorizationProvider,
    ILogger<AuthorizationBehavior<TRequest, TResponse>> logger,
    AuthorizationResourceContextResolver? resourceContextResolver = null,
    IAuthorizationContextEnricher<TRequest>? authorizationContextEnricher = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly RequestAuthorization<TRequest> _authorization = new(
        authorizationProvider, resourceContextResolver, authorizationContextEnricher);

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        await _authorization.AuthorizeAsync(request, logger, cancellationToken);
        return await next(cancellationToken);
    }
}
