using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Behaviors;

/// <summary>
/// Authoritative enforcement point for command and query authorization.
/// <para>
/// Resource context is resolved in increasing order of trust: the request may declare typed facts,
/// a per-request enricher may replace them, and the shared
/// <see cref="AuthorizationResourceContextResolver"/> overrides both wherever it can load the resource
/// server-side. Only the surviving typed facts reach the provider — the pipeline never forwards a
/// caller-authored policy dictionary.
/// </para>
/// </summary>
public sealed class AuthorizationBehavior<TRequest, TResponse>(
    IAuthorizationProvider authorizationProvider,
    ILogger<AuthorizationBehavior<TRequest, TResponse>> logger,
    AuthorizationResourceContextResolver? resourceContextResolver = null,
    IAuthorizationContextEnricher<TRequest>? authorizationContextEnricher = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly ConcurrentDictionary<Type, AuthorizeResourceAttribute?> AttributeCache = new();
    private static readonly ActivitySource AuthorizationActivitySource = new("Explore.Authorization");
    private readonly AuthorizationResourceContextResolver _resourceContextResolver = resourceContextResolver ?? new();

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var attribute = AttributeCache.GetOrAdd(typeof(TRequest), static t => t.GetCustomAttribute<AuthorizeResourceAttribute>());
        if (attribute is null)
        {
            return await next(cancellationToken);
        }

        var resourceId = typeof(TRequest).Name;
        IAuthorizationFacts? facts = null;

        if (request is ISecureRequest secureRequest)
        {
            resourceId = secureRequest.ResourceId ?? resourceId;
            facts = secureRequest.AuthorizationFacts;
        }

        if (authorizationContextEnricher is not null)
        {
            var context = await authorizationContextEnricher.ResolveAsync(request, cancellationToken);
            resourceId = context.ResourceId ?? resourceId;
            facts = context.Facts ?? facts;
        }

        var resolvedContext = await _resourceContextResolver.ResolveAsync(
            request,
            attribute.Resource,
            attribute.Action,
            resourceId,
            facts,
            cancellationToken);

        await EnforceAuthorizationAsync(
            attribute.Resource,
            resolvedContext.ResourceId ?? resourceId,
            attribute.Action,
            resolvedContext.Facts,
            typeof(TRequest).Name,
            cancellationToken);

        return await next(cancellationToken);
    }

    private async Task EnforceAuthorizationAsync(
        string resourceKind,
        string resourceId,
        string action,
        IAuthorizationFacts? facts,
        string requestType,
        CancellationToken cancellationToken)
    {
        using var activity = AuthorizationActivitySource.StartActivity("authorization.evaluate");
        activity?.SetTag("resource.kind", resourceKind);
        activity?.SetTag("resource.action", action);
        activity?.SetTag("request.type", requestType);
        var correlationId = Activity.Current?.Id ?? string.Empty;

        var decision = await authorizationProvider.AuthorizeAsync(
            new AuthorizationRequest(
                AuthorizationCapabilityCatalog.Require(resourceKind, action),
                resourceId,
                Facts: facts),
            cancellationToken);

        if (!decision.IsAllowed)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Authorization denied");
            logger.LogWarning(
                "Authorization decision: {Decision} request={RequestType} resource={Resource} action={Action} reason={Reason} provider={Provider} correlationId={CorrelationId}",
                "deny", requestType, resourceKind, action, decision.ReasonCode, decision.Provider.ProviderId, correlationId);

            if (string.Equals(
                    decision.ReasonCode,
                    AuthorizationDecisionReasonCodes.ProviderUnavailable,
                    StringComparison.Ordinal))
            {
                throw new AuthorizationProviderUnavailableException(resourceKind, action);
            }

            throw new AuthorizationException(resourceKind, action);
        }

        logger.LogDebug(
            "Authorization decision: {Decision} request={RequestType} resource={Resource} action={Action} provider={Provider} correlationId={CorrelationId}",
            "allow", requestType, resourceKind, action, decision.Provider.ProviderId, correlationId);
    }
}
