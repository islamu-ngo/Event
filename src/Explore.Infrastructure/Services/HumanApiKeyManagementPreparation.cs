using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.ExternalApiKeys.Requests.Commands;
using Explore.Application.Features.ExternalApiKeys.Requests.Queries;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Keeps tenant API-key management on its original authenticated request and physical owner.
/// Only quota selects a PDP decision; revoke/details retain their existing owner predicates.
/// </summary>
public sealed class HumanApiKeyManagementPreparation(
    IHumanCommitScopePreparation humanScope,
    IHttpContextAccessor httpContext,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    IMachinePrincipalAccessor machine,
    ITenantContext tenantContext,
    TimeProvider clock,
    CerbosAuthorizationService cerbos,
    IOptions<AuthorizationProviderDeploymentOptions> authorizationOptions,
    IOptions<CerbosSettings> cerbosOptions,
    Func<IHumanPrincipalSourceFence> resolvePrincipal,
    Func<IHumanAuthorizationRouteSource> resolveRoute,
    Func<IHumanSupportAccessSourceFactory> resolveSupport,
    Func<IPolicySourceSetFence> resolveExclusion,
    Func<IHumanActionCommitFinalizer> resolveFinalizer) : IHumanApiKeyManagementPreparation
{
    public Task<IPreparedHumanApiKeyManagement?> PrepareAsync(UpdateExternalApiKeyPolicyCommand request,
        Guid principalId, CancellationToken cancellationToken = default)
    {
        string originalContent = JsonSerializer.Serialize(request);
        void RequireContent()
        {
            if (!string.Equals(originalContent, JsonSerializer.Serialize(request), StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The original API-key policy request changed.");
        }
        return PrepareAsync(request, request.ExternalApiKeyId, principalId, AuthorizationActions.Update,
            false, cancellationToken, RequireContent);
    }

    public Task<IPreparedHumanApiKeyManagement?> PrepareAsync(RevokeExternalApiKeyCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareAsync(request, request.Id, principalId, AuthorizationActions.Delete, false, cancellationToken);

    public Task<IPreparedHumanApiKeyManagement?> PrepareAsync(GetExternalApiKeyDetailsRequest request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareAsync(request, request.Id, principalId, AuthorizationActions.View, false, cancellationToken);

    public Task<IPreparedHumanApiKeyManagement?> PrepareAsync(GetExternalApiKeyUsageReportRequest request,
        Guid principalId, CancellationToken cancellationToken = default)
    {
        if (request.TenantId != tenantContext.TenantId || request.From > request.To)
            throw new AuthorizationException("The exact tenant report request is required.");
        return PrepareAsync(request, request.TenantId ?? Guid.Empty, principalId,
            AuthorizationActions.View, true, cancellationToken);
    }

    private async Task<IPreparedHumanApiKeyManagement?> PrepareAsync(object operation, Guid target,
        Guid principalId, string action, bool quota, CancellationToken cancellationToken, Action? requireContent = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (machine.IsMachineCaller) return null;
        if (target == Guid.Empty || principalId == Guid.Empty || tenantContext.TenantId == Guid.Empty)
            throw new AuthorizationException("An exact tenant API-key management target is required.");
        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("Original human management authority is required.");
        var request = httpContext.HttpContext
            ?? throw new UnauthorizedAccessException("The original authenticated request is required.");
        var authenticatedPrincipal = request.User;
        var lifetime = await lifetimeObserver.ObserveAsync(authenticatedPrincipal, cancellationToken)
            ?? throw new UnauthorizedAccessException("Original validated credential evidence is required.");
        Guid tenantId = tenantContext.TenantId;
        if (principalId != subject.PrincipalId || lifetime.AccountKey != subject.AccountKey
            || !lifetime.IsValidAt(clock.GetUtcNow()))
            throw new UnauthorizedAccessException("The original credential does not authorize this subject.");

        void RequireCustody()
        {
            requireContent?.Invoke();
            if (machine.IsMachineCaller || !ReferenceEquals(humanScope.Subject, subject)
                || tenantContext.TenantId != tenantId || !ReferenceEquals(httpContext.HttpContext, request)
                || !ReferenceEquals(request.User, authenticatedPrincipal))
                throw new UnauthorizedAccessException("Original API-key management custody has changed.");
        }

        var principalSource = resolvePrincipal();
        var sourceSetFence = resolveExclusion();
        var supportSource = await resolveSupport().CreateAsync(cancellationToken);
        var principalFacts = await principalSource.ObserveAsync(cancellationToken);
        var supportFacts = await supportSource.ObserveAsync(cancellationToken);
        void RequirePrincipal(HumanPrincipalSourceFacts facts)
        {
            RequireCustody();
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, facts);
            if (facts.SelectedTenantId != tenantId)
                throw new UnauthorizedAccessException("Management sources belong to another selected tenant.");
        }
        RequirePrincipal(principalFacts);
        // This is the existing Tenant/view question for quota. For keys it supplies only
        // the support boundary's trusted tenant/action, never a new Tenant policy grant.
        var check = new AuthorizationRequest(ResourceKinds.Tenant, tenantId.ToString("D"), action,
            Facts: new TenantScopedAuthorizationFacts(tenantId));
        RequireSupport(supportFacts, clock.GetUtcNow(), check);
        IHumanAuthorizationRouteSource? routeSource = null;
        Action<HumanPrincipalSourceFacts, HumanAuthorizationRouteFacts>? compareSelectedInputs = null;
        if (quota)
        {
            routeSource = resolveRoute();
            var routeFacts = await routeSource.ObserveAsync(cancellationToken);
            var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
            var route = HumanAuthorizationRouteProjector.Project(routeFacts, deployment, false,
                lifetime.LocalSession is null || principalFacts.LocalSession is not null);
            var projectedPrincipal = CerbosPrincipalBuilder.BuildPrincipalFromSources(principalFacts);
            var serialized = CerbosPrincipalBuilder.CapturePrincipal(projectedPrincipal);
            var localInputs = FallbackAuthorizationService.PreparedTenantInputs(principalFacts, check, tenantId);
            compareSelectedInputs = (currentPrincipal, currentRoute) =>
            {
                if (currentRoute.PrincipalId != subject.PrincipalId || currentRoute.SelectedTenantId != tenantId)
                    throw new UnauthorizedAccessException("Quota route belongs to another original owner.");
                var projected = HumanAuthorizationRouteProjector.Project(currentRoute, deployment, false,
                    lifetime.LocalSession is null || currentPrincipal.LocalSession is not null);
                bool matches = route.Provider == HumanAuthorizationRouteProvider.Local
                    ? localInputs == FallbackAuthorizationService.PreparedTenantInputs(currentPrincipal, check, tenantId)
                    : serialized.Equals(CerbosPrincipalBuilder.CapturePrincipal(
                        CerbosPrincipalBuilder.BuildPrincipalFromSources(currentPrincipal)));
                if (!matches || !route.IsSemanticallyEquivalentTo(projected))
                    throw new UnauthorizedAccessException("The original quota selected-provider inputs have changed.");
            };
            compareSelectedInputs(principalFacts, routeFacts);
            bool allowed;
            if (route.Provider == HumanAuthorizationRouteProvider.Local)
                allowed = FallbackAuthorizationService.EvaluatePreparedTenant(principalFacts, check, tenantId, false);
            else if (route.Provider is HumanAuthorizationRouteProvider.CerbosInstance or HumanAuthorizationRouteProvider.CerbosTenant)
            {
                var decision = await cerbos.AuthorizePreparedHumanAsync(projectedPrincipal, check, tenantId,
                    deployment.UsePolicyScope, route.Endpoint!, cancellationToken);
                if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                    throw new AuthorizationProviderUnavailableException(check.ResourceKind, check.Action);
                allowed = decision.IsAllowed;
            }
            else allowed = false;
            if (!allowed) throw new AuthorizationException(check.ResourceKind, check.Action);
        }
        RequireCustody();
        return new Prepared(operation, principalId, async token =>
        {
            RequireCustody();
            var sourceSet = await sourceSetFence.AcquireAsync(token);
            var principal = await principalSource.AcquireAsync(sourceSet, token);
            var route = routeSource is null ? null : await routeSource.AcquireAsync(sourceSet, token);
            var support = await supportSource.AcquireAsync(sourceSet, principal, cancellationToken: token);
            RequirePrincipal(principal.Facts);
            if (route is not null) compareSelectedInputs!(principal.Facts, route.Facts);
            var acquiredAt = clock.GetUtcNow();
            if (!lifetime.IsValidAt(acquiredAt))
                throw new UnauthorizedAccessException("The original management credential expired before acquisition.");
            RequireSupport(supportFacts, acquiredAt, check);
            RequireSupport(support.Facts, acquiredAt, check);
            HumanSupportAccessSourceFacts? finalSupport = null;
            resolveFinalizer().Register(async finalToken =>
            {
                await sourceSet.ValidateAsync(finalToken);
                var finalPrincipal = await principal.ValidateAsync(finalToken);
                var finalRoute = route is null ? null : await route.ValidateAsync(finalToken);
                finalSupport = await support.ValidateAsync(finalToken);
                RequirePrincipal(finalPrincipal);
                if (finalRoute is not null) compareSelectedInputs!(finalPrincipal, finalRoute);
            }, now =>
            {
                RequireCustody();
                if (!lifetime.IsValidAt(now))
                    throw new UnauthorizedAccessException("The original management credential expired before release.");
                RequireSupport(supportFacts, now, check);
                RequireSupport(finalSupport
                    ?? throw new InvalidOperationException("Final management sources have not been admitted."), now, check);
            });
        });
    }

    private static void RequireSupport(HumanSupportAccessSourceFacts facts, DateTimeOffset now,
        AuthorizationRequest check)
    {
        if (HumanSupportAccessProjection.BoundaryDenialReason(facts, now, check.ResourceKind, check.Action,
                AuthorizationFactAttributeProjection.ToAttributes(check.Facts)) is not null)
            throw new UnauthorizedAccessException("Original support authority does not permit API-key management.");
    }

    private sealed class Prepared(object operation, Guid principalId, Func<CancellationToken, Task> acquire)
        : IPreparedHumanApiKeyManagement
    {
        private int consumed;

        public Task AcquireAsync(object originalRequest, Guid originalPrincipalId,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref consumed, 1) != 0)
                throw new InvalidOperationException("Prepared management authority belongs to one attempt.");
            if (!ReferenceEquals(operation, originalRequest) || principalId != originalPrincipalId)
                throw new UnauthorizedAccessException("Management authority belongs to another original request.");
            cancellationToken.ThrowIfCancellationRequested();
            return acquire(cancellationToken);
        }
    }
}
