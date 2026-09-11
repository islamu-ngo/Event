using System.Diagnostics;
using System.Reflection;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Authorization;

/// <summary>
/// Evaluates the existing request authorization contract in increasing trust order:
/// request facts, typed enrichment, persisted resource facts, then the selected provider.
/// Unannotated public, worker and handler-owned authorities remain enforced by their owners.
/// </summary>
public sealed class RequestAuthorization<TRequest>(
    IAuthorizationProvider authorizationProvider,
    AuthorizationResourceContextResolver? resourceContextResolver = null,
    IAuthorizationContextEnricher<TRequest>? authorizationContextEnricher = null)
    where TRequest : notnull
{
    private static readonly AuthorizeResourceAttribute? Attribute = typeof(TRequest).GetCustomAttribute<AuthorizeResourceAttribute>();
    private static readonly ActivitySource AuthorizationActivitySource = new("Explore.Authorization");
    private readonly AuthorizationResourceContextResolver _resourceContextResolver = resourceContextResolver ?? new();

    public async Task AuthorizeAsync(TRequest request, ILogger logger, CancellationToken cancellationToken)
    {
        if (Attribute is null)
            return;

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
            request, Attribute.Resource, Attribute.Action, resourceId, facts, cancellationToken);

        using var activity = AuthorizationActivitySource.StartActivity("authorization.evaluate");
        activity?.SetTag("resource.kind", Attribute.Resource);
        activity?.SetTag("resource.action", Attribute.Action);
        activity?.SetTag("request.type", typeof(TRequest).Name);
        var correlationId = Activity.Current?.Id ?? string.Empty;
        var decision = await authorizationProvider.AuthorizeAsync(
            new AuthorizationRequest(
                AuthorizationCapabilityCatalog.Require(Attribute.Resource, Attribute.Action),
                resolvedContext.ResourceId ?? resourceId,
                Facts: resolvedContext.Facts),
            cancellationToken);

        if (!decision.IsAllowed)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Authorization denied");
            logger.LogWarning(
                "Authorization decision: {Decision} request={RequestType} resource={Resource} action={Action} reason={Reason} provider={Provider} correlationId={CorrelationId}",
                "deny", typeof(TRequest).Name, Attribute.Resource, Attribute.Action, decision.ReasonCode, decision.Provider.ProviderId, correlationId);
            if (string.Equals(decision.ReasonCode, AuthorizationDecisionReasonCodes.ProviderUnavailable, StringComparison.Ordinal))
                throw new AuthorizationProviderUnavailableException(Attribute.Resource, Attribute.Action);
            throw new AuthorizationException(Attribute.Resource, Attribute.Action);
        }
        logger.LogDebug(
            "Authorization decision: {Decision} request={RequestType} resource={Resource} action={Action} provider={Provider} correlationId={CorrelationId}",
            "allow", typeof(TRequest).Name, Attribute.Resource, Attribute.Action, decision.Provider.ProviderId, correlationId);
    }
}
