using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;
using Explore.Application.Features.EventTemplates.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Domain.Enums;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Original-source admission for template and definition administration and tenant-plan draft creation.
/// The existing administrator predicate and unchanged annotated question are both required.
/// No selected connection, mutation purpose, or Settings capability is created here.
/// </summary>
public sealed partial class HumanAdministrativeActionPreparation(
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
    Func<IHumanActionCommitFinalizer> resolveFinalizer,
    Func<AuthorizationResourceContextResolver> resolveResource) : IHumanAdministrativeActionPreparation
{
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(CreateEventTemplateCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);

    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(UpdateEventTemplateCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);

    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(DeleteEventTemplateCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);

    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(CreateCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);

    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(UpdateCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);

    private Task<IPreparedHumanAdministrativeAction> PrepareTenantActionAsync(
        object request, Guid principalId, CancellationToken cancellationToken)
    {
        string originalContent = JsonSerializer.Serialize(request, request.GetType());
        void RequireContent()
        {
            if (!string.Equals(originalContent, JsonSerializer.Serialize(request, request.GetType()), StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The original administrative request content changed.");
        }
        return PrepareAsync(request, principalId, instance: false, RequireContent, cancellationToken);
    }

    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(DeleteCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareAsync(request, principalId, instance: false, requireContent: null, cancellationToken);

    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(CreateInstanceTenantPlanDraftCommand request,
        Guid principalId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // A record does not make its caller-supplied collections immutable. Keep the
        // original draft content, not just an equal record or a borrowed list reference.
        var settings = request.Draft.SettingOverrides.ToArray();
        var quotas = request.Draft.QuotaLimits.ToArray();
        void RequireDraft()
        {
            if (!settings.SequenceEqual(request.Draft.SettingOverrides)
                || !quotas.SequenceEqual(request.Draft.QuotaLimits))
                throw new UnauthorizedAccessException("The original administrative draft content changed.");
        }
        return PrepareAsync(request, principalId, instance: true, RequireDraft, cancellationToken);
    }

    private async Task<IPreparedHumanAdministrativeAction> PrepareAsync(object operation, Guid principalId,
        bool instance, Action? requireContent, CancellationToken cancellationToken,
        string? resourceKind = null, bool revalidateResource = false)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (machine.IsMachineCaller || principalId == Guid.Empty)
            throw new UnauthorizedAccessException("Original authenticated human administration is required.");
        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("The original resolved administrator is required.");
        var request = httpContext.HttpContext
            ?? throw new UnauthorizedAccessException("The original authenticated request is required.");
        var authenticatedPrincipal = request.User;
        var lifetime = await lifetimeObserver.ObserveAsync(authenticatedPrincipal, cancellationToken)
            ?? throw new UnauthorizedAccessException("Authentic original credential lifetime evidence is required.");
        Guid ambientTenant = tenantContext.TenantId;
        if (principalId != subject.PrincipalId || lifetime.AccountKey != subject.AccountKey
            || !instance && ambientTenant == Guid.Empty || !lifetime.IsValidAt(clock.GetUtcNow()))
            throw new UnauthorizedAccessException("The original administrative credential or target is invalid.");
        void Custody()
        {
            requireContent?.Invoke();
            if (machine.IsMachineCaller || !ReferenceEquals(humanScope.Subject, subject)
                || !ReferenceEquals(httpContext.HttpContext, request)
                || !ReferenceEquals(request.User, authenticatedPrincipal) || tenantContext.TenantId != ambientTenant)
                throw new UnauthorizedAccessException("The original administrative request custody changed.");
        }
        Custody();
        var principalSource = resolvePrincipal();
        var routeSource = resolveRoute();
        var supportSource = await resolveSupport().CreateAsync(cancellationToken);
        var principalFacts = await principalSource.ObserveAsync(cancellationToken);
        var routeFacts = await routeSource.ObserveAsync(cancellationToken);
        var originalSupport = await supportSource.ObserveAsync(cancellationToken);
        // Instance administration may already be constructed on an ordinary Global or
        // Runtime owner. Preserve that exact selection; an instance action is not permission
        // to substitute a Global context for the handler's data context.
        Guid? selectedTenant = principalFacts.SelectedTenantId;
        void Facts(HumanPrincipalSourceFacts principal, HumanAuthorizationRouteFacts currentRoute)
        {
            Custody();
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, principal);
            if (principal.SelectedTenantId != selectedTenant || currentRoute.SelectedTenantId != selectedTenant
                || currentRoute.PrincipalId != subject.PrincipalId
                || !instance && selectedTenant != ambientTenant
                || instance && selectedTenant.HasValue && selectedTenant != ambientTenant)
                throw new UnauthorizedAccessException("Administrative sources belong to another original owner.");
        }
        Facts(principalFacts, routeFacts);
        var secure = (ISecureRequest)operation;
        string kind = resourceKind ?? (instance ? ResourceKinds.InstanceSetting : ResourceKinds.Tenant);
        string action = instance ? AuthorizationActions.InstanceSettings.Update : AuthorizationActions.Update;
        string declaredId = secure.ResourceId ?? operation.GetType().Name;
        var resourceResolver = resolveResource();
        var originalResource = await resourceResolver.ResolveAsync(
            operation, kind, action, declaredId, secure.AuthorizationFacts, cancellationToken);
        var check = new AuthorizationRequest(kind, originalResource.ResourceId ?? declaredId, action,
            Facts: originalResource.Facts);
        async Task RequireOriginalResourceAsync(CancellationToken token)
        {
            var current = await resourceResolver.ResolveAsync(operation, kind, action, declaredId,
                secure.AuthorizationFacts, token);
            if (!string.Equals(current.ResourceId, originalResource.ResourceId, StringComparison.Ordinal)
                || !Equals(current.Facts, originalResource.Facts))
                throw new UnauthorizedAccessException("The original administrative resource inputs changed.");
        }
        var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
        var route = HumanAuthorizationRouteProjector.Project(routeFacts, deployment, settingsGovernance: instance,
            localDecisionSourcesCoLocated: lifetime.LocalSession is null || principalFacts.LocalSession is not null);
        var projectedPrincipal = CerbosPrincipalBuilder.BuildPrincipalFromSources(principalFacts);
        var serialized = CerbosPrincipalBuilder.CapturePrincipal(projectedPrincipal);
        var localInputs = FallbackAuthorizationService.PreparedTenantInputs(principalFacts, check, ambientTenant);
        bool originalInstanceAdmin = InstanceAdministrator(principalFacts);
        bool originalTenantAdmin = !instance && !originalInstanceAdmin && TenantAdministrator(principalFacts, ambientTenant);
        RequireSupport(originalSupport, clock.GetUtcNow(), check);
        bool allowed;
        if (route.Provider == HumanAuthorizationRouteProvider.Local)
            allowed = instance ? originalInstanceAdmin
                : FallbackAuthorizationService.EvaluatePreparedAdministrativeResource(principalFacts, check, ambientTenant);
        else if (route.Provider == HumanAuthorizationRouteProvider.CerbosInstance
            || !instance && route.Provider == HumanAuthorizationRouteProvider.CerbosTenant)
        {
            var decision = await cerbos.AuthorizePreparedHumanAsync(projectedPrincipal, check, ambientTenant,
                deployment.UsePolicyScope, route.Endpoint!, cancellationToken);
            if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                throw new AuthorizationProviderUnavailableException(check.ResourceKind, check.Action);
            allowed = decision.IsAllowed;
        }
        else allowed = false;
        if (!allowed) throw new AuthorizationException(check.ResourceKind, check.Action);

        void Compare(HumanPrincipalSourceFacts principal, HumanAuthorizationRouteFacts currentRoute)
        {
            Facts(principal, currentRoute);
            var projected = HumanAuthorizationRouteProjector.Project(currentRoute, deployment, settingsGovernance: instance,
                localDecisionSourcesCoLocated: lifetime.LocalSession is null || principal.LocalSession is not null);
            bool principalMatches = route.Provider == HumanAuthorizationRouteProvider.Local
                ? instance ? originalInstanceAdmin == InstanceAdministrator(principal)
                    : localInputs == FallbackAuthorizationService.PreparedTenantInputs(principal, check, ambientTenant)
                : serialized.Equals(CerbosPrincipalBuilder.CapturePrincipal(
                    CerbosPrincipalBuilder.BuildPrincipalFromSources(principal)));
            // These are the existing commit ports' predicates, not a new policy route.
            // A later role cannot restore an originally absent administrator contributor.
            if (!route.IsSemanticallyEquivalentTo(projected) || !principalMatches
                || !principalFacts.Bindings.SequenceEqual(principal.Bindings)
                || originalInstanceAdmin != InstanceAdministrator(principal)
                || !instance && !originalInstanceAdmin
                    && originalTenantAdmin != TenantAdministrator(principal, ambientTenant))
                throw new UnauthorizedAccessException("The original administrative authority inputs changed.");
        }
        void Time(HumanSupportAccessSourceFacts support, DateTimeOffset now)
        {
            Custody();
            if (!lifetime.IsValidAt(now))
                throw new UnauthorizedAccessException("The original administrative credential expired.");
            RequireSupport(originalSupport, now, check);
            RequireSupport(support, now, check);
        }
        Time(originalSupport, clock.GetUtcNow());
        var exclusionOwner = resolveExclusion();
        return new Prepared(operation, principalId, async token =>
        {
            Custody();
            var exclusion = await exclusionOwner.AcquireAsync(token);
            var principal = await principalSource.AcquireAsync(exclusion, token);
            var currentRoute = await routeSource.AcquireAsync(exclusion, token);
            var support = await supportSource.AcquireAsync(exclusion, principal, cancellationToken: token);
            await RequireOriginalResourceAsync(token);
            Compare(principal.Facts, currentRoute.Facts);
            Time(support.Facts, clock.GetUtcNow());
            HumanSupportAccessSourceFacts? finalSupport = null;
            resolveFinalizer().Register(async finalToken =>
            {
                await exclusion.ValidateAsync(finalToken);
                var finalPrincipal = await principal.ValidateAsync(finalToken);
                var finalRoute = await currentRoute.ValidateAsync(finalToken);
                finalSupport = await support.ValidateAsync(finalToken);
                if (revalidateResource) await RequireOriginalResourceAsync(finalToken);
                Compare(finalPrincipal, finalRoute);
            }, now => Time(finalSupport
                ?? throw new InvalidOperationException("Terminal administrative sources have not been admitted."), now));
        });
    }

    private static bool InstanceAdministrator(HumanPrincipalSourceFacts principal) =>
        principal.PlatformGrants.Any(grant => principal.Roles.Any(role => role.Id == grant.RoleId
            && role.RoleScopeId == (int)RoleScopeEnum.Platform && role.MatchesPlatformAdministratorCode));

    private static bool TenantAdministrator(HumanPrincipalSourceFacts principal, Guid tenantId) =>
        principal.Tenants.Any(tenant => tenant.Id == tenantId && tenant.IsActiveState)
        && principal.TenantMemberships.Count(member => member.TenantId == tenantId
            && member.StatusId == (int)TenantUserStatusEnum.Active && !member.IsDeleted) == 1
        && principal.TenantGrants.Any(grant => grant.TenantId == tenantId
            && principal.TenantMemberships.Any(member => member.Id == grant.TenantUserId && member.TenantId == tenantId
                && member.StatusId == (int)TenantUserStatusEnum.Active && !member.IsDeleted)
            && grant.RoleId == (int)RoleEnum.TenantAdmin && grant.RoleScopeId == (int)RoleScopeEnum.Tenant
            && grant.RevokedAt is null && principal.Roles.Any(role => role.Id == grant.RoleId
                && role.RoleScopeId == (int)RoleScopeEnum.Tenant));

    private static void RequireSupport(HumanSupportAccessSourceFacts facts, DateTimeOffset now, AuthorizationRequest check)
    {
        // InstanceSetting is not a tenant-bound resource. That does not allow an
        // originally forwarded session to outlive its real horizon (the platform-key
        // issuer keeps this same original-support continuity requirement).
        if (facts.Forwarding.WasForwarded && !HumanSupportAccessProjection.Context(facts, now).IsActive
            || HumanSupportAccessProjection.BoundaryDenialReason(facts, now, check.ResourceKind, check.Action,
                AuthorizationFactAttributeProjection.ToAttributes(check.Facts)) is not null)
            throw new UnauthorizedAccessException("Original support authority does not permit this administrative action.");
    }

    private sealed class Prepared(object operation, Guid principalId, Func<CancellationToken, Task> acquire)
        : IPreparedHumanAdministrativeAction
    {
        private int consumed;

        public Task AcquireAsync(object originalRequest, Guid originalPrincipalId,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref consumed, 1) != 0)
                throw new InvalidOperationException("Prepared administrative authority belongs to one attempt.");
            if (!ReferenceEquals(operation, originalRequest) || principalId != originalPrincipalId)
                throw new UnauthorizedAccessException("Administrative authority belongs to another original action.");
            cancellationToken.ThrowIfCancellationRequested();
            return acquire(cancellationToken);
        }
    }
}
