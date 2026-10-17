using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Tenants.Requests.Commands;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Prepares one exact Tenant question outside transactions and issues a private one-attempt
/// capability. Acquisition and final admission only compare original inputs; they never call
/// a PDP, rebuild an ambient principal or substitute an administrator predicate.
/// </summary>
public sealed class HumanTenantMutationPreparation(
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
    Func<IHumanActionCommitFinalizer> resolveFinalizer,
    Func<ISerializableAttemptOwner> resolveOwner) : IHumanTenantMutationPreparation
{
    public async Task<bool> ExecuteDeletionAsync(DeleteTenantCommand request, ITenantRepository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(repository);
        var originalRequest = httpContext.HttpContext
            ?? throw new UnauthorizedAccessException("The original authenticated request is required.");
        var originalPrincipal = originalRequest.User;
        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(httpContext.HttpContext, originalRequest)
                || !ReferenceEquals(originalRequest.User, originalPrincipal))
                throw new UnauthorizedAccessException("Tenant deletion cannot replace the original request on retry.");
            var issued = (Prepared)await PrepareAsync(HumanTenantMutationKind.Delete, request.Id, cancellationToken);
            try
            {
                return await resolveOwner().ExecuteAsync(async token =>
                {
                    // Discovery capture/retention own this reservation before source locks.
                    // This exact deletion seam keeps that order inside the original owner.
                    await repository.AcquireHumanDeletionReservationAsync(request.Id, token);
                    await issued.AcquireAsync(request.Id, HumanTenantMutationKind.Delete, token);
                    issued.OwnerActive = true;
                    try
                    {
                        return await repository.DeleteHumanAsync(request.Id, issued, token);
                    }
                    catch (Exception exception) when (ContainsRollbackMarker(exception))
                    {
                        throw new InvalidOperationException(
                            "Tenant deletion cannot issue a physical rollback acknowledgment.");
                    }
                    finally
                    {
                        issued.OwnerActive = false;
                    }
                }, cancellationToken);
            }
            catch (HumanMutationAttemptRolledBackException) when (attempt < 4)
            {
                // Only the original owner's acknowledged rollback permits a new preparation.
            }
        }
    }

    public async Task<T> ExecuteAsync<T>(HumanTenantMutationKind mutation, Guid tenantId,
        Func<IPreparedHumanTenantMutation, CancellationToken, Task<T>> originalOwnerAttempt,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prepared = await PrepareAsync(mutation, tenantId, cancellationToken);
            try
            {
                return await ExecutePreparedAsync(mutation, tenantId, prepared, originalOwnerAttempt, cancellationToken);
            }
            catch (HumanMutationAttemptRolledBackException) when (attempt < 4)
            {
                // The original owner's acknowledged rollback already cleared its state.
                // Reobserve all sources and obtain a new selected-provider result next.
            }
        }
    }

    public Task<T> ExecutePreparedAsync<T>(HumanTenantMutationKind mutation, Guid tenantId,
        IPreparedHumanTenantMutation prepared,
        Func<IPreparedHumanTenantMutation, CancellationToken, Task<T>> originalOwnerAttempt,
        CancellationToken cancellationToken = default)
    {
        if (mutation == HumanTenantMutationKind.Delete)
            throw new InvalidOperationException("Tenant deletion requires its reservation-first execution seam.");
        if (prepared is not Prepared issued || !ReferenceEquals(issued.Issuer, this)
            || issued.TenantId != tenantId || issued.Mutation != mutation)
            throw new UnauthorizedAccessException("The original Tenant preparation issuer and target are required.");
        return resolveOwner().ExecuteAsync(async token =>
        {
            await issued.AcquireAsync(tenantId, mutation, token);
            issued.OwnerActive = true;
            try
            {
                return await originalOwnerAttempt(prepared, token);
            }
            catch (Exception exception) when (ContainsRollbackMarker(exception))
            {
                throw new InvalidOperationException(
                    "A Tenant callback cannot issue a physical rollback acknowledgment.");
            }
            finally
            {
                issued.OwnerActive = false;
            }
        }, cancellationToken);
    }

    public async Task<IPreparedHumanTenantMutation> PrepareAsync(HumanTenantMutationKind mutation, Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mutation) || tenantId == Guid.Empty)
            throw new ArgumentException("An exact Tenant mutation target is required.");
        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("An authenticated human is required.");
        var request = httpContext.HttpContext
            ?? throw new UnauthorizedAccessException("The original authenticated request is required.");
        var principal = request.User;
        var lifetime = await lifetimeObserver.ObserveAsync(principal, cancellationToken)
            ?? throw new UnauthorizedAccessException("Original credential lifetime evidence is required.");
        if (lifetime.AccountKey != subject.AccountKey || !lifetime.IsValidAt(clock.GetUtcNow()))
            throw new UnauthorizedAccessException("The original credential is no longer valid.");

        var principalSource = resolvePrincipal();
        var routeSource = resolveRoute();
        var supportSource = await resolveSupport().CreateAsync(cancellationToken);
        var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
        Guid ambientTenant = tenantContext.TenantId;
        var principalFacts = await principalSource.ObserveAsync(cancellationToken);
        var routeFacts = await routeSource.ObserveAsync(cancellationToken);
        var supportFacts = await supportSource.ObserveAsync(cancellationToken);
        if (routeFacts.SelectedTenantId is { } selected && selected != tenantId
            || routeFacts.SelectedTenantId is null && ambientTenant != Guid.Empty)
            throw new UnauthorizedAccessException("The original selected owner must match the Tenant action.");
        HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, principalFacts);
        var check = new AuthorizationRequest(ResourceKinds.Tenant, tenantId.ToString("D"),
            mutation == HumanTenantMutationKind.Delete ? AuthorizationActions.Delete : AuthorizationActions.Update,
            Facts: new TenantScopedAuthorizationFacts(tenantId));
        var route = HumanAuthorizationRouteProjector.Project(routeFacts, deployment, settingsGovernance: false,
            localDecisionSourcesCoLocated: lifetime.LocalSession is null || principalFacts.LocalSession is not null);
        RequireSupport(supportFacts, clock.GetUtcNow(), check);
        var projection = CerbosPrincipalBuilder.BuildPrincipalFromSources(principalFacts);
        var serialized = CerbosPrincipalBuilder.CapturePrincipal(projection);
        var localInputs = FallbackAuthorizationService.PreparedTenantInputs(principalFacts, check, ambientTenant);
        bool allowed;
        if (route.Provider == HumanAuthorizationRouteProvider.Local)
            allowed = FallbackAuthorizationService.EvaluatePreparedTenant(principalFacts, check, ambientTenant, false);
        else if (route.Provider is HumanAuthorizationRouteProvider.CerbosInstance or HumanAuthorizationRouteProvider.CerbosTenant)
        {
            var decision = await cerbos.AuthorizePreparedHumanAsync(projection, check, ambientTenant,
                deployment.UsePolicyScope, route.Endpoint!, cancellationToken);
            if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                throw new AuthorizationProviderUnavailableException(check.ResourceKind, check.Action);
            allowed = decision.IsAllowed;
        }
        else
            allowed = false;
        if (!allowed)
            throw new AuthorizationException(check.ResourceKind, check.Action);
        void RequireCustody()
        {
            if (!ReferenceEquals(httpContext.HttpContext, request) || !ReferenceEquals(request.User, principal)
                || !ReferenceEquals(humanScope.Subject, subject) || tenantContext.TenantId != ambientTenant)
                throw new UnauthorizedAccessException("The original Tenant request custody has changed.");
        }
        RequireCustody();
        if (!lifetime.IsValidAt(clock.GetUtcNow()))
            throw new UnauthorizedAccessException("The original preparation custody has changed.");
        RequireSupport(supportFacts, clock.GetUtcNow(), check);
        return new Prepared(this, humanScope, tenantContext, clock, resolveExclusion, resolveFinalizer,
            subject, lifetime, mutation, tenantId, ambientTenant, check,
            principalSource, routeSource, supportSource, supportFacts, deployment, route, serialized, localInputs,
            RequireCustody);
    }

    public Task RequireOwnedAsync(IPreparedHumanTenantMutation prepared, Guid tenantId,
        HumanTenantMutationKind mutation, CancellationToken cancellationToken = default)
    {
        if (prepared is not Prepared issued || !ReferenceEquals(issued.Issuer, this)
            || !issued.OwnerActive || issued.TenantId != tenantId || issued.Mutation != mutation)
            throw new UnauthorizedAccessException("The original Tenant owner admission is required.");
        return issued.ValidateOwnerAsync(cancellationToken);
    }

    private static bool ContainsRollbackMarker(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is HumanMutationAttemptRolledBackException) return true;
        return exception is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsRollbackMarker);
    }

    private static void RequireSupport(HumanSupportAccessSourceFacts facts, DateTimeOffset now,
        AuthorizationRequest check)
    {
        if (HumanSupportAccessProjection.BoundaryDenialReason(facts, now, check.ResourceKind, check.Action,
                AuthorizationFactAttributeProjection.ToAttributes(check.Facts)) is not null)
            throw new UnauthorizedAccessException("The original support boundary does not permit the action.");
    }

    private sealed class Prepared(
        HumanTenantMutationPreparation issuer,
        IHumanCommitScopePreparation humanScope, ITenantContext tenantContext, TimeProvider clock,
        Func<IPolicySourceSetFence> resolveExclusion, Func<IHumanActionCommitFinalizer> resolveFinalizer,
        HumanCommitSubject subject, HumanAuthenticationLifetime lifetime,
        HumanTenantMutationKind mutation, Guid tenantId, Guid ambientTenant, AuthorizationRequest check,
        IHumanPrincipalSourceFence principalSource, IHumanAuthorizationRouteSource routeSource,
        IHumanSupportAccessSourceFence supportSource, HumanSupportAccessSourceFacts originalSupport,
        HumanAuthorizationRouteDeployment deployment,
        HumanAuthorizationRoute route, Cerbos.Api.V1.Engine.Principal serialized,
        (bool InstanceAdmin, bool TenantAdmin, Guid ResourceTenant) localInputs,
        Action requireCustody) : IPreparedHumanTenantMutation
    {
        private int _consumed;
        private Func<CancellationToken, Task>? _validateOwner;
        internal HumanTenantMutationPreparation Issuer => issuer;
        internal Guid TenantId => tenantId;
        internal HumanTenantMutationKind Mutation => mutation;
        internal bool OwnerActive { get; set; }
        internal Task ValidateOwnerAsync(CancellationToken token) =>
            _validateOwner is null
                ? throw new UnauthorizedAccessException("The original Tenant owner has not acquired admission.")
                : _validateOwner(token);

        public async Task AcquireAsync(Guid target, HumanTenantMutationKind operation,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _consumed, 1) != 0)
                throw new InvalidOperationException("A prepared action belongs to one attempt only.");
            requireCustody();
            if (target != tenantId || operation != mutation
                || !ReferenceEquals(humanScope.Subject, subject) || tenantContext.TenantId != ambientTenant)
                throw new UnauthorizedAccessException("The prepared action belongs to another original target or subject.");
            var exclusion = await resolveExclusion().AcquireAsync(cancellationToken);
            var principal = await principalSource.AcquireAsync(exclusion, cancellationToken);
            var currentRoute = await routeSource.AcquireAsync(exclusion, cancellationToken);
            var support = await supportSource.AcquireAsync(exclusion, principal, cancellationToken: cancellationToken);
            if (support.Facts.Forwarding != originalSupport.Forwarding)
                throw new UnauthorizedAccessException("The original Tenant support selector has changed.");
            Compare(principal.Facts, currentRoute.Facts);
            var acquiredAt = clock.GetUtcNow();
            if (!lifetime.IsValidAt(acquiredAt))
                throw new UnauthorizedAccessException("The prepared original credential has expired.");
            RequireSupport(support.Facts, acquiredAt, check);
            RequireSupport(originalSupport, acquiredAt, check);
            _validateOwner = async token =>
            {
                await principal.ValidateAsync(token);
                await currentRoute.ValidateAsync(token);
            };
            HumanSupportAccessSourceFacts? finalSupport = null;
            resolveFinalizer().Register(async token =>
            {
                var finalPrincipal = await principal.ValidateAsync(token);
                var finalRoute = await currentRoute.ValidateAsync(token);
                finalSupport = await support.ValidateAsync(token);
                if (finalSupport.Forwarding != originalSupport.Forwarding)
                    throw new UnauthorizedAccessException("The original Tenant support selector has changed.");
                Compare(finalPrincipal, finalRoute);
            }, now =>
            {
                requireCustody();
                if (!lifetime.IsValidAt(now))
                    throw new UnauthorizedAccessException("The prepared original credential has expired.");
                RequireSupport(finalSupport!, now, check);
                RequireSupport(originalSupport, now, check);
            });
        }

        private void Compare(HumanPrincipalSourceFacts principal, HumanAuthorizationRouteFacts routeFacts)
        {
            requireCustody();
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, principal);
            var current = HumanAuthorizationRouteProjector.Project(routeFacts, deployment, false,
                lifetime.LocalSession is null || principal.LocalSession is not null);
            bool principalMatches = route.Provider == HumanAuthorizationRouteProvider.Local
                ? localInputs == FallbackAuthorizationService.PreparedTenantInputs(principal, check, ambientTenant)
                : serialized.Equals(CerbosPrincipalBuilder.CapturePrincipal(
                    CerbosPrincipalBuilder.BuildPrincipalFromSources(principal)));
            if (!route.IsSemanticallyEquivalentTo(current) || !principalMatches)
                throw new UnauthorizedAccessException("The original selected-provider inputs have changed.");
        }
    }
}
