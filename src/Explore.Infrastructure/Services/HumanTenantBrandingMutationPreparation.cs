using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Commands;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Exact branding action preparation from current principal, platform settings route and
/// original support sources. The single-use capability never calls a PDP under owner locks.
/// Field governance and document CAS remain independent requirements in the actual handler.
/// </summary>
public sealed class HumanTenantBrandingMutationPreparation(
    IHumanCommitScopePreparation humanScope,
    IHttpContextAccessor httpContext,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    ITenantContext tenantContext,
    TimeProvider clock,
    CerbosAuthorizationService cerbos,
    IOptions<AuthorizationProviderDeploymentOptions> authorizationOptions,
    IOptions<CerbosSettings> cerbosOptions,
    Func<IHumanPrincipalSourceFence> resolvePrincipal,
    Func<IHumanAuthorizationRouteSource> resolveRoute,
    Func<IHumanSupportAccessSourceFactory> resolveSupport,
    Func<IPolicySourceSetFence> resolveExclusion,
    Func<IHumanActionCommitFinalizer> resolveFinalizer) : IHumanTenantBrandingMutationPreparation
{
    public async Task<IPreparedHumanTenantBrandingMutation> PrepareAsync(
        PatchTenantBrandingSettingsDocumentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.TenantId == Guid.Empty)
            throw new ArgumentException("An exact branding tenant is required.", nameof(command));
        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("An original authenticated human is required.");
        var request = httpContext.HttpContext
            ?? throw new UnauthorizedAccessException("The original authenticated request is required.");
        var authenticatedPrincipal = request.User;
        var lifetime = await lifetimeObserver.ObserveAsync(authenticatedPrincipal, cancellationToken)
            ?? throw new UnauthorizedAccessException("Original credential lifetime evidence is required.");
        if (lifetime.AccountKey != subject.AccountKey || !lifetime.IsValidAt(clock.GetUtcNow()))
            throw new UnauthorizedAccessException("The original credential is no longer valid.");

        void RequireCustody()
        {
            if (!ReferenceEquals(humanScope.Subject, subject) || tenantContext.TenantId != command.TenantId
                || !ReferenceEquals(httpContext.HttpContext, request)
                || !ReferenceEquals(request.User, authenticatedPrincipal))
                throw new UnauthorizedAccessException("The original branding execution custody has changed.");
        }
        RequireCustody();

        // Resolve source contracts only after canonical scope selection. Independent Local
        // identity remains unsupported: never relabel its session as co-located evidence.
        var principalSource = resolvePrincipal();
        var routeSource = resolveRoute();
        var exclusionSource = resolveExclusion();
        var supportSource = await resolveSupport().CreateAsync(cancellationToken);
        var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
        var principalFacts = await principalSource.ObserveAsync(cancellationToken);
        var routeFacts = await routeSource.ObserveAsync(cancellationToken);
        var supportFacts = await supportSource.ObserveAsync(cancellationToken);
        RequireFacts(subject, lifetime, command.TenantId, principalFacts, routeFacts);
        var check = new AuthorizationRequest(ResourceKinds.TenantSetting, command.TenantId.ToString("D"),
            AuthorizationActions.Update,
            Facts: new TenantSettingAuthorizationFacts(command.TenantId, SettingsDocumentKeys.Tenant.Branding));
        // RuntimeAuthorizationProvider sends settings to the platform provider, never tenant BYO.
        var route = HumanAuthorizationRouteProjector.Project(routeFacts, deployment, settingsGovernance: true,
            localDecisionSourcesCoLocated: lifetime.LocalSession is null || principalFacts.LocalSession is not null);
        RequireSupport(supportFacts, clock.GetUtcNow(), check);
        var principal = CerbosPrincipalBuilder.BuildPrincipalFromSources(principalFacts);
        var serialized = CerbosPrincipalBuilder.CapturePrincipal(principal);
        bool localTenantAdmin = IsTenantAdministrator(principalFacts, command.TenantId);
        bool allowed;
        if (route.Provider == HumanAuthorizationRouteProvider.Local)
            allowed = localTenantAdmin;
        else if (route.Provider == HumanAuthorizationRouteProvider.CerbosInstance)
        {
            var decision = await cerbos.AuthorizePreparedHumanAsync(principal, check, command.TenantId,
                deployment.UsePolicyScope, route.Endpoint!, cancellationToken);
            if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                throw new AuthorizationProviderUnavailableException(check.ResourceKind, check.Action);
            allowed = decision.IsAllowed;
        }
        else allowed = false;
        if (!allowed) throw new AuthorizationException(check.ResourceKind, check.Action);
        RequireCustody();
        if (!lifetime.IsValidAt(clock.GetUtcNow()))
            throw new UnauthorizedAccessException("The original credential expired during branding preparation.");

        void Compare(HumanPrincipalSourceFacts currentPrincipal, HumanAuthorizationRouteFacts currentRoute)
        {
            RequireCustody();
            RequireFacts(subject, lifetime, command.TenantId, currentPrincipal, currentRoute);
            var projected = HumanAuthorizationRouteProjector.Project(currentRoute, deployment, settingsGovernance: true,
                localDecisionSourcesCoLocated: lifetime.LocalSession is null || currentPrincipal.LocalSession is not null);
            if (route.Provider == HumanAuthorizationRouteProvider.Local
                && !IsTenantAdministrator(currentPrincipal, command.TenantId))
                throw new AuthorizationException(check.ResourceKind, check.Action);
            bool principalMatches = route.Provider == HumanAuthorizationRouteProvider.Local
                ? localTenantAdmin == IsTenantAdministrator(currentPrincipal, command.TenantId)
                : serialized.Equals(CerbosPrincipalBuilder.CapturePrincipal(
                    CerbosPrincipalBuilder.BuildPrincipalFromSources(currentPrincipal)));
            if (!route.IsSemanticallyEquivalentTo(projected) || !principalMatches)
                throw new UnauthorizedAccessException("The original branding selected-provider inputs have changed.");
        }

        return new Prepared(command, async token =>
        {
            RequireCustody();
            var exclusion = await exclusionSource.AcquireAsync(token);
            var principalLease = await principalSource.AcquireAsync(exclusion, token);
            var routeLease = await routeSource.AcquireAsync(exclusion, token);
            var supportLease = await supportSource.AcquireAsync(exclusion, principalLease, cancellationToken: token);
            Compare(principalLease.Facts, routeLease.Facts);
            var acquiredAt = clock.GetUtcNow();
            if (!lifetime.IsValidAt(acquiredAt))
                throw new UnauthorizedAccessException("The original credential expired before branding acquisition.");
            RequireSupport(supportFacts, acquiredAt, check);
            RequireSupport(supportLease.Facts, acquiredAt, check);
            HumanSupportAccessSourceFacts? finalSupport = null;
            resolveFinalizer().Register(async finalToken =>
            {
                var currentPrincipal = await principalLease.ValidateAsync(finalToken);
                var currentRoute = await routeLease.ValidateAsync(finalToken);
                finalSupport = await supportLease.ValidateAsync(finalToken);
                Compare(currentPrincipal, currentRoute);
            }, now =>
            {
                RequireCustody();
                if (!lifetime.IsValidAt(now))
                    throw new UnauthorizedAccessException("The original credential expired before branding commit.");
                // Own writes cannot stretch the original prepared support horizon.
                RequireSupport(supportFacts, now, check);
                RequireSupport(finalSupport
                    ?? throw new InvalidOperationException("Final branding sources have not been admitted."), now, check);
            });
        });
    }

    private static void RequireFacts(HumanCommitSubject subject, HumanAuthenticationLifetime lifetime,
        Guid tenantId, HumanPrincipalSourceFacts principal, HumanAuthorizationRouteFacts route)
    {
        HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, principal);
        if (principal.SelectedTenantId != tenantId || route.SelectedTenantId != tenantId
            || route.PrincipalId != subject.PrincipalId)
            throw new UnauthorizedAccessException("Branding sources belong to another original subject or tenant.");
    }

    private static bool IsTenantAdministrator(HumanPrincipalSourceFacts principal, Guid tenantId) =>
        // FallbackAuthorizationService's exact TenantSetting/update path has no platform-admin
        // shortcut. Branding's per-field locks are enforced by the handler, not a blanket lock.
        principal.TenantGrants.Any(grant => grant.TenantId == tenantId
            && grant.RoleId == (int)RoleEnum.TenantAdmin && grant.RevokedAt is null
            && principal.TenantMemberships.Any(member => member.Id == grant.TenantUserId
                && member.StatusId == (int)TenantUserStatusEnum.Active && !member.IsDeleted));

    private static void RequireSupport(HumanSupportAccessSourceFacts facts, DateTimeOffset now,
        AuthorizationRequest check)
    {
        if (HumanSupportAccessProjection.BoundaryDenialReason(facts, now, check.ResourceKind, check.Action,
                AuthorizationFactAttributeProjection.ToAttributes(check.Facts)) is not null)
            throw new UnauthorizedAccessException("The original support boundary does not permit branding changes.");
    }

    private sealed class Prepared(PatchTenantBrandingSettingsDocumentCommand command,
        Func<CancellationToken, Task> acquire) : IPreparedHumanTenantBrandingMutation
    {
        private int consumed;

        public Task AcquireAsync(PatchTenantBrandingSettingsDocumentCommand originalCommand,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref consumed, 1) != 0)
                throw new InvalidOperationException("A prepared branding action belongs to one attempt only.");
            if (!ReferenceEquals(command, originalCommand))
                throw new UnauthorizedAccessException("The prepared branding action belongs to another command.");
            cancellationToken.ThrowIfCancellationRequested();
            return acquire(cancellationToken);
        }
    }
}
