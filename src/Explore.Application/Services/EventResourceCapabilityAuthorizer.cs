using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;

namespace Explore.Application.Services;

/// <summary>Connects native capability requests to server-owned resource identity and fresh bulk authority.</summary>
public sealed class EventResourceCapabilityAuthorizer(
    EventResourceAuthorityOrchestrator authority,
    ICurrentUserService currentUser,
    ITenantContext tenantContext,
    IMachinePrincipalAccessor machinePrincipal) : IEventResourceCapabilityAuthorizer
{
    public async Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
        IReadOnlyList<AuthorizationRequest> requests, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = requests.Select(_ => AuthorizationDecision.Deny(
            AuthorizationProviderMetadata.Runtime, AuthorizationDecisionReasonCodes.InvalidRequest)).ToArray();
        if (requests.Count == 0 || requests.Count > EventResourceAuthorityRequest.MaximumBatchChecks) return results;
        Guid tenantId = tenantContext.TenantId;
        bool machine = machinePrincipal.IsMachineCaller;
        Guid? userId = !machine && currentUser.IsAuthenticated ? currentUser.UserId : null;
        if (tenantId == Guid.Empty || userId == Guid.Empty) return results;
        var targets = new List<EventResourceAuthorityRequest>();
        var positions = new List<int>();
        for (int index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            if (request.ResourceKind != ResourceKinds.EventResource
                || !Guid.TryParse(request.ResourceId, out Guid resourceId) || resourceId == Guid.Empty
                || request.Subject?.UserId is { } subject && subject != userId
                || request.Subject?.IsMachine == true && !machine
                || request.Tenant?.TenantId is { } declaredTenant && declaredTenant != tenantId
                || request.Tenant?.OrganizationId is not null || request.Scope?.OrganizationId is not null
                || request.Scope?.TenantId is { } scope
                    && (!Guid.TryParse(scope, out Guid scopedTenant) || scopedTenant != tenantId))
                continue;
            bool validTarget = request.Facts switch
            {
                null => true,
                EventResourceTargetAuthorizationFacts target => target.TenantId == tenantId && target.ResourceId == resourceId,
                EventResourceCollectionAuthorizationFacts collection => collection.TenantId == tenantId
                    && collection.EventId == resourceId && request.Action is "view-management" or "export",
                _ => false
            };
            if (!validTarget) continue;
            positions.Add(index);
            targets.Add(new(tenantId, resourceId, userId, machine, request.Action)
            {
                IsEventCollection = request.Facts is EventResourceCollectionAuthorizationFacts
            });
        }
        if (targets.Count == 0) return results;
        var decisions = await authority.AuthorizeCapabilitiesAsync(targets, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (decisions.Count != targets.Count) return results;
        for (int index = 0; index < decisions.Count; index++)
        {
            var outcome = decisions[index];
            results[positions[index]] = outcome == EventResourceAuthorityOutcome.Allowed
                ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Runtime)
                : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Runtime,
                    outcome is EventResourceAuthorityOutcome.Unavailable or EventResourceAuthorityOutcome.Expired
                        ? AuthorizationDecisionReasonCodes.ProviderUnavailable : AuthorizationDecisionReasonCodes.Denied);
        }
        return results;
    }
}
