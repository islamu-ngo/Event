using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain.Enums;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Retains the original human, unchanged selected question and original support horizon.
/// Successful self-transitions change only their compiled membership/grant image; they never
/// obtain a second policy decision using the resulting role or a refreshed credential.
/// </summary>
public sealed partial class HumanAccountAdministrativeActionPreparation(
    IHumanCommitScopePreparation humanScope,
    IHttpContextAccessor httpContext,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    IMachinePrincipalAccessor machine,
    ITenantContext tenantContext,
    TimeProvider clock,
    CerbosAuthorizationService cerbos,
    IOptions<AuthorizationProviderDeploymentOptions> authorizationOptions,
    IOptions<CerbosSettings> cerbosOptions,
    Func<AuthorizationResourceContextResolver> resolveResource,
    Func<IHumanPrincipalSourceFence> resolvePrincipal,
    Func<IHumanAuthorizationRouteSource> resolveRoute,
    Func<IHumanSupportAccessSourceFactory> resolveSupport,
    Func<IPolicySourceSetFence> resolveExclusion,
    Func<IAccountMembershipPolicySource> resolvePolicy,
    Func<IHumanActionCommitFinalizer> resolveFinalizer) : IHumanAccountAdministrativeActionPreparation
{
    private async Task<IPreparedHumanAccountAdministrativeAction> Prepare(
        object operation, Guid? requester, string resource, string action, bool annotated,
        CancellationToken cancellationToken)
    {
        if (machine.IsMachineCaller)
            throw new UnauthorizedAccessException("Account administration requires original human custody.");
        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("The original account subject is required.");
        var context = httpContext.HttpContext
            ?? throw new UnauthorizedAccessException("The original authenticated request is required.");
        var authenticated = context.User;
        var lifetime = await lifetimeObserver.ObserveAsync(authenticated, cancellationToken)
            ?? throw new UnauthorizedAccessException("Authentic credential lifetime is required.");
        Guid tenant = tenantContext.TenantId;
        if (tenant == Guid.Empty || subject.AccountKey != lifetime.AccountKey
            || requester.HasValue && (requester == Guid.Empty || requester != subject.PrincipalId))
            throw new UnauthorizedAccessException("The requester is not the original resolved account.");
        if (operation is ISecureRequest secure && secure.AuthorizationFacts switch
        {
            TenantScopedAuthorizationFacts scoped => scoped.TenantId != tenant,
            OrganizationMemberAuthorizationFacts scoped => scoped.TenantId != tenant,
            _ => false
        })
            throw new UnauthorizedAccessException("The account question does not belong to the selected tenant.");
        // PostgreSQL retains microseconds. The closed transition and persisted
        // revocation must use the same timestamp rather than compare lost ticks.
        DateTime changedAt = clock.GetUtcNow().UtcDateTime;
        changedAt = new DateTime(changedAt.Ticks - changedAt.Ticks % 10, DateTimeKind.Utc);
        var prepared = new Prepared(operation, subject.PrincipalId, changedAt);
        void Custody()
        {
            if (prepared.Rejected || machine.IsMachineCaller || !ReferenceEquals(humanScope.Subject, subject)
                || !ReferenceEquals(httpContext.HttpContext, context) || !ReferenceEquals(context.User, authenticated)
                || tenantContext.TenantId != tenant)
                throw new UnauthorizedAccessException("Original account request custody changed.");
        }
        var principalSource = resolvePrincipal();
        var routeSource = resolveRoute();
        var exclusionSource = resolveExclusion();
        var supportSource = await resolveSupport().CreateAsync(cancellationToken);
        var policySource = resolvePolicy();
        var original = await principalSource.ObserveAsync(cancellationToken);
        var routeFacts = await routeSource.ObserveAsync(cancellationToken);
        var originalSupport = await supportSource.ObserveAsync(cancellationToken);
        var target = Target(operation);
        var policy = await policySource.ObserveAsync(target, cancellationToken);
        var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
        var route = Project(routeFacts, original);
        var check = await Question(cancellationToken);
        // P and the retained definition inventory were observed separately. Reject a
        // torn preparation instead of authorizing old P with newer role definitions.
        RequireSources(original, routeFacts, completed: false);
        RequireTime(originalSupport, clock.GetUtcNow());

        // GroupMember commands have no annotation. Their existing owner performs the
        // permission-or-GroupAdmin predicate under held sources, not a fabricated PDP check.
        if (annotated)
        {
            bool allowed;
            if (route.Provider == HumanAuthorizationRouteProvider.Local)
                allowed = LocalAllowed(original, check, tenant);
            else if (route.Provider is HumanAuthorizationRouteProvider.CerbosInstance or HumanAuthorizationRouteProvider.CerbosTenant)
            {
                var decision = await cerbos.AuthorizePreparedHumanAsync(CerbosPrincipalBuilder.BuildPrincipalFromSources(original),
                    check, tenant, deployment.UsePolicyScope, route.Endpoint!, cancellationToken);
                if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                    throw new AuthorizationProviderUnavailableException(resource, action);
                allowed = decision.IsAllowed;
            }
            else allowed = false;
            if (!allowed) throw new AuthorizationException(resource, action);
        }

        prepared.Acquire = async token =>
        {
            Custody();
            // Fixed ordering: S, then P/route/H, then the retained definitions and
            // original resource facts. No handler business read precedes these leases.
            var exclusion = await exclusionSource.AcquireAsync(token);
            var principal = await principalSource.AcquireAsync(exclusion, token);
            var currentRoute = await routeSource.AcquireAsync(exclusion, token);
            var support = await supportSource.AcquireAsync(exclusion, principal, cancellationToken: token);
            var definitions = await policySource.AcquireAsync(target, exclusion, token);
            RequirePolicy(definitions.Facts, completed: false);
            RequireSources(principal.Facts, currentRoute.Facts, completed: false);
            var acquiredQuestion = await Question(token);
            if (acquiredQuestion != check)
                throw new UnauthorizedAccessException("The original account policy question changed.");
            RequireTime(support.Facts, clock.GetUtcNow());
            HumanSupportAccessSourceFacts? finalSupport = null;
            resolveFinalizer().Register(async finalToken =>
            {
                await exclusion.ValidateAsync(finalToken);
                RequirePolicy(await definitions.ValidateAsync(finalToken), prepared.Completed);
                var finalPrincipal = await principal.ValidateAsync(finalToken);
                var finalRoute = await currentRoute.ValidateAsync(finalToken);
                finalSupport = await support.ValidateAsync(finalToken);
                RequireSources(finalPrincipal, finalRoute, prepared.Completed);
                // Read disclosure keeps its original persisted question through the last
                // blocking source read. Mutations use their closed expected transition.
                if (action == AuthorizationActions.View && await Question(finalToken) != check)
                    throw new UnauthorizedAccessException("The original disclosure target changed.");
            }, now => RequireTime(finalSupport
                ?? throw new InvalidOperationException("Account terminal sources were not admitted."), now));
        };
        return prepared;

        async Task<AuthorizationRequest> Question(CancellationToken token)
        {
            if (!annotated)
                return new AuthorizationRequest(resource, operation.GetType().Name, action,
                    Facts: new TenantScopedAuthorizationFacts(tenant));
            var secure = (ISecureRequest)operation;
            string id = secure.ResourceId ?? operation.GetType().Name;
            // Collection/create annotations already name an organization, not a
            // membership ID. Preserve that exact declared question. Detail/update/delete
            // continue to use the existing persisted membership enrichment.
            if (operation is Application.Features.OrganizationMembers.Requests.Commands.AddOrganizationMemberCommand
                or Application.Features.OrganizationMembers.Requests.Queries.GetOrganizationMembersRequest)
                return new AuthorizationRequest(resource, id, action, Facts: secure.AuthorizationFacts);
            var facts = await resolveResource().ResolveAsync(operation, resource, action, id, secure.AuthorizationFacts, token);
            return new AuthorizationRequest(resource, facts.ResourceId ?? id, action, Facts: facts.Facts);
        }

        HumanAuthorizationRoute Project(HumanAuthorizationRouteFacts facts, HumanPrincipalSourceFacts principal) =>
            HumanAuthorizationRouteProjector.Project(facts, deployment, settingsGovernance: false,
                localDecisionSourcesCoLocated: lifetime.LocalSession is null || principal.LocalSession is not null);

        void RequirePrincipal(HumanPrincipalSourceFacts facts)
        {
            Custody();
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, facts);
            if (facts.SelectedTenantId != tenant || routeFacts.SelectedTenantId != tenant
                || routeFacts.PrincipalId != subject.PrincipalId)
                throw new UnauthorizedAccessException("Account sources belong to another selected owner.");
        }

        void RequirePolicy(AccountMembershipPolicyFacts current, bool completed)
        {
            if (!policy.Roles.SequenceEqual(current.Roles) || !policy.Permissions.SequenceEqual(current.Permissions))
                throw new UnauthorizedAccessException("Original account role or permission definitions changed.");
            var expectedTarget = completed ? ExpectedTarget(operation, policy.Target, prepared) : policy.Target;
            if (current.Target != expectedTarget)
                throw new UnauthorizedAccessException("The original account target changed outside its exact transition.");
        }

        void RequireSources(HumanPrincipalSourceFacts current, HumanAuthorizationRouteFacts currentRoute, bool completed)
        {
            RequirePrincipal(current);
            if (currentRoute.PrincipalId != subject.PrincipalId || currentRoute.SelectedTenantId != tenant
                || !route.IsSemanticallyEquivalentTo(Project(currentRoute, current)))
                throw new UnauthorizedAccessException("The original account authorization route changed.");
            var expected = completed ? Expected(operation, original, prepared) : original;
            if (!Same(expected.Users, current.Users) || !Same(expected.Bindings, current.Bindings)
                || !Same(expected.PlatformGrants, current.PlatformGrants)
                || !Same(expected.TenantMemberships, current.TenantMemberships) || !Same(expected.Tenants, current.Tenants)
                || !Same(expected.TenantGrants, current.TenantGrants)
                || !Same(expected.Organizations, current.Organizations) || !Same(expected.Groups, current.Groups)
                || original.PermissionMappingsExist != current.PermissionMappingsExist)
                throw new UnauthorizedAccessException("An unrelated original account contributor changed.");
            var roleIds = expected.PlatformGrants.Select(row => row.RoleId)
                .Concat(expected.TenantGrants.Select(row => row.RoleId))
                .Concat(expected.Organizations.Select(row => row.RoleId))
                .Concat(expected.Groups.Select(row => row.RoleId)).ToHashSet();
            if (!Same(policy.Roles.Where(row => roleIds.Contains(row.Id)), current.Roles)
                || !Same(policy.Permissions.Where(row => roleIds.Contains(row.RoleId)), current.Permissions))
                throw new UnauthorizedAccessException("Account definitions do not match the exact expected transition.");
            // Retained complete definitions and exact membership images cover the inputs
            // of derived permission lists after self-demotion. Before acquisition, retain
            // the original provider's enumeration order as well.
            if (!completed && (!original.EventCreateOrganizations.SequenceEqual(current.EventCreateOrganizations)
                || !original.EventCreateGroups.SequenceEqual(current.EventCreateGroups)
                || !original.EventFinanceOrganizations.SequenceEqual(current.EventFinanceOrganizations)
                || !original.EventFinanceGroups.SequenceEqual(current.EventFinanceGroups)))
                throw new UnauthorizedAccessException("Original provider permission inputs changed.");
        }

        void RequireTime(HumanSupportAccessSourceFacts support, DateTimeOffset now)
        {
            Custody();
            if (!lifetime.IsValidAt(now))
                throw new UnauthorizedAccessException("The original account credential expired.");
            RequireSupport(originalSupport, now);
            RequireSupport(support, now);
        }

        void RequireSupport(HumanSupportAccessSourceFacts support, DateTimeOffset now)
        {
            if (support.Forwarding.WasForwarded && !HumanSupportAccessProjection.Context(support, now).IsActive
                || HumanSupportAccessProjection.BoundaryDenialReason(support, now, resource, action,
                    AuthorizationFactAttributeProjection.ToAttributes(check.Facts)) is not null)
                throw new UnauthorizedAccessException("Original support access no longer permits the account action.");
        }
    }

    private static bool Same<T>(IEnumerable<T> expected, IEnumerable<T> actual) where T : notnull
    {
        var counts = expected.GroupBy(row => row).ToDictionary(group => group.Key, group => group.Count());
        var groups = actual.GroupBy(row => row).ToArray();
        return counts.Count == groups.Length
            && groups.All(group => counts.TryGetValue(group.Key, out int count) && count == group.Count());
    }

    private static bool LocalAllowed(HumanPrincipalSourceFacts facts, AuthorizationRequest check, Guid tenant)
    {
        bool instanceAdmin = facts.PlatformGrants.Any(grant => facts.Roles.Any(role => role.Id == grant.RoleId
            && role.RoleScopeId == (int)RoleScopeEnum.Platform && role.MatchesPlatformAdministratorCode));
        if (check.ResourceKind == ResourceKinds.TenantUserRoleGrant && instanceAdmin) return true;
        Guid requestedTenant = check.Facts switch
        {
            TenantScopedAuthorizationFacts scoped => scoped.TenantId,
            OrganizationMemberAuthorizationFacts member => member.TenantId,
            _ => tenant
        };
        bool tenantAdmin = requestedTenant == tenant
            && facts.TenantGrants.Any(grant => grant.TenantId == tenant && grant.RoleId == (int)RoleEnum.TenantAdmin
                && grant.RevokedAt is null
                && facts.TenantMemberships.Any(member => member.Id == grant.TenantUserId && member.TenantId == tenant
                    && !member.IsDeleted && member.StatusId == (int)TenantUserStatusEnum.Active));
        if (tenantAdmin) return true;
        return check.ResourceKind == ResourceKinds.OrganizationMember
            && check.Facts is OrganizationMemberAuthorizationFacts organization
            && facts.Organizations.Any(member => member.TenantId == tenant && member.OwnerId == organization.OrganizationId
                && !member.IsDeleted && !member.PlacementIsDeleted && !member.OwnerIsDeleted
                && member.RoleId == (int)RoleEnum.OrgAdmin);
    }

    private sealed class Prepared(object operation, Guid principalId, DateTime changedAt)
        : IPreparedHumanAccountAdministrativeAction
    {
        private int consumed;
        private bool acquired;
        internal Func<CancellationToken, Task> Acquire { private get; set; } = null!;
        internal bool Completed { get; private set; }
        internal bool Rejected { get; private set; }
        internal Guid TargetId { get; private set; }
        public Guid PrincipalId { get; } = principalId;
        public DateTime ChangedAt { get; } = changedAt;

        public async Task AcquireAsync(object originalRequest, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref consumed, 1) != 0)
            {
                Rejected = true;
                throw new InvalidOperationException("Prepared account custody belongs to one attempt.");
            }
            if (!ReferenceEquals(operation, originalRequest))
            {
                Rejected = true;
                throw new UnauthorizedAccessException("Account custody belongs to another original request.");
            }
            try
            {
                await Acquire(cancellationToken);
                acquired = true;
            }
            catch
            {
                Rejected = true;
                throw;
            }
        }

        public void Complete(Guid targetId)
        {
            if (!acquired || Completed || targetId == Guid.Empty)
            {
                Rejected = true;
                throw new InvalidOperationException("Account transition completion requires one acquired body.");
            }
            try { RequireTarget(operation, targetId); }
            catch
            {
                Rejected = true;
                throw;
            }
            TargetId = targetId;
            Completed = true;
        }
    }
}
