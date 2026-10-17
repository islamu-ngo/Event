using Cerbos.Sdk.Builder;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

/// <summary>
/// One original Event/manage-team decision, with its authentic credential and support
/// horizons. Assignment changes are closed transitions, not fresh role authorization.
/// </summary>
public sealed partial class HumanEventTeamActionPreparation(
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
    Func<IHumanEventAssignmentSourceFactory> resolveAssignments,
    Func<IHumanSupportAccessSourceFactory> resolveSupport,
    Func<IPolicySourceSetFence> resolveExclusion,
    Func<IEventTeamPolicySource> resolvePolicy,
    Func<IHumanActionCommitFinalizer> resolveFinalizer) : IHumanEventTeamActionPreparation
{
    public Task<IPreparedHumanEventTeamAction> PrepareAsync(AssignEventRoleByEmailCommand request, CancellationToken token) => Prepare(request, token);
    public Task<IPreparedHumanEventTeamAction> PrepareAsync(AssignEventRoleCommand request, CancellationToken token) => Prepare(request, token);
    public Task<IPreparedHumanEventTeamAction> PrepareAsync(RevokeEventRoleAssignmentCommand request, CancellationToken token) => Prepare(request, token);
    public Task<IPreparedHumanEventTeamAction> PrepareAsync(GetEventTeamListRequest request, CancellationToken token) => Prepare(request, token);
    public Task<IPreparedHumanEventTeamAction> PrepareAsync(GetAssignableEventRolePresetsRequest request, CancellationToken token) => Prepare(request, token);

    public void ConsumeHandoff(AssignEventRoleCommand request, IPreparedHumanEventTeamAction prepared)
    {
        if (prepared is not Prepared original)
            throw new UnauthorizedAccessException("The Event team handoff belongs to another selected execution scope.");
        if (!ReferenceEquals(original.Issuer, this))
        {
            original.Rejected = true;
            throw new UnauthorizedAccessException("The Event team handoff belongs to another selected execution scope.");
        }
        original.ConsumeHandoff(request);
    }

    private async Task<IPreparedHumanEventTeamAction> Prepare(object operation, CancellationToken token)
    {
        var (tenant, eventId, actor) = operation switch
        {
            AssignEventRoleByEmailCommand x => (x.TenantId, x.EventId, (Guid?)x.ActorUserId),
            AssignEventRoleCommand x => (x.TenantId, x.EventId, (Guid?)x.ActorUserId),
            RevokeEventRoleAssignmentCommand x => (x.TenantId, x.EventId, (Guid?)x.ActorUserId),
            GetEventTeamListRequest x => (x.TenantId, x.EventId, null),
            GetAssignableEventRolePresetsRequest x => (x.TenantId, x.EventId, (Guid?)x.AssignerUserId),
            _ => throw new InvalidOperationException("Unknown original Event team operation.")
        };
        if (machine.IsMachineCaller)
            throw new UnauthorizedAccessException("Event team administration requires original human custody.");
        await humanScope.PrepareAsync(token);
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("The original resolved human is required.");
        var context = httpContext.HttpContext
            ?? throw new UnauthorizedAccessException("The original authenticated request is required.");
        var authenticated = context.User;
        var lifetime = await lifetimeObserver.ObserveAsync(authenticated, token)
            ?? throw new UnauthorizedAccessException("Authentic original credential lifetime is required.");
        if (tenant == Guid.Empty || eventId == Guid.Empty || tenantContext.TenantId != tenant
            || subject.AccountKey != lifetime.AccountKey || actor.HasValue && actor != subject.PrincipalId)
            throw new UnauthorizedAccessException("Event team target or actor differs from original custody.");

        var principalSource = resolvePrincipal();
        var routeSource = resolveRoute();
        var assignmentSource = resolveAssignments().Create(tenant, [eventId]);
        var supportSource = await resolveSupport().CreateAsync(token);
        var policySource = resolvePolicy();
        var exclusionSource = resolveExclusion();
        var target = operation switch
        {
            AssignEventRoleByEmailCommand x => new EventTeamSourceTarget(eventId, Email: x.TargetUserEmail.Trim()),
            AssignEventRoleCommand x => new EventTeamSourceTarget(eventId, UserId: x.TargetUserId),
            _ => new EventTeamSourceTarget(eventId)
        };
        var original = await principalSource.ObserveAsync(token);
        var originalRoute = await routeSource.ObserveAsync(token);
        var originalSupport = await supportSource.ObserveAsync(token);
        DateTime evaluation = clock.GetUtcNow().UtcDateTime;
        var assignments = await assignmentSource.ObserveAsync(token);
        DateTime providerNow = clock.GetUtcNow().UtcDateTime;
        var policy = await policySource.ObserveAsync(target, token);
        var resource = HumanEventAssignmentProjection.Resource(assignments, eventId)
            ?? throw new UnauthorizedAccessException("The original persisted Event is unavailable.");
        var question = new AuthorizationRequest(ResourceKinds.Event, eventId.ToString("D"),
            AuthorizationActions.Events.ManageTeam, Facts: resource);
        var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
        HumanAuthorizationRoute Route(HumanAuthorizationRouteFacts facts, HumanPrincipalSourceFacts principal) =>
            HumanAuthorizationRouteProjector.Project(facts, deployment, false,
                lifetime.LocalSession is null || principal.LocalSession is not null);
        var route = Route(originalRoute, original);
        var prepared = new Prepared(this, operation, policy, assignments, evaluation,
            Canonical(clock.GetUtcNow().UtcDateTime), subject.PrincipalId);
        var originalProviderPrincipal = Principal(original, assignments, evaluation);
        RequireSources(original, originalRoute, assignments, policy);
        RequireTime(originalSupport, clock.GetUtcNow());
        bool allowed;
        if (route.Provider == HumanAuthorizationRouteProvider.Local)
            allowed = LocalAllowed(original, assignments, resource, evaluation);
        else if (route.Provider is HumanAuthorizationRouteProvider.CerbosInstance or HumanAuthorizationRouteProvider.CerbosTenant)
        {
            var decision = await cerbos.AuthorizePreparedHumanAsync(BuildPrincipal(original, assignments, evaluation),
                question, tenant, deployment.UsePolicyScope, route.Endpoint!, token);
            if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                throw new AuthorizationProviderUnavailableException(question.ResourceKind, question.Action);
            allowed = decision.IsAllowed;
        }
        else allowed = false;
        if (!allowed) throw new AuthorizationException(question.ResourceKind, question.Action);
        RequireTime(originalSupport, clock.GetUtcNow());

        prepared.Acquire = async cancellation =>
        {
            Custody();
            // No business lookup, email resolution or audit precedes S/P/assignment/H acquisition.
            var exclusion = await exclusionSource.AcquireAsync(cancellation);
            var principal = await principalSource.AcquireAsync(exclusion, cancellation);
            var selectedRoute = await routeSource.AcquireAsync(exclusion, cancellation);
            var assignment = await assignmentSource.AcquireAsync(exclusion, cancellation);
            var support = await supportSource.AcquireAsync(exclusion, principal, assignment, cancellation);
            var definitions = await policySource.AcquireAsync(target, exclusion, cancellation);
            prepared.RequireActiveOwner = () =>
            {
                Custody();
                definitions.RequireOwner();
            };
            RequireSources(principal.Facts, selectedRoute.Facts, assignment.Facts, definitions.Facts);
            RequireTime(support.Facts, clock.GetUtcNow());
            HumanSupportAccessSourceFacts? finalSupport = null;
            resolveFinalizer().Register(async finalToken =>
            {
                await exclusion.ValidateAsync(finalToken);
                var finalPrincipal = await principal.ValidateAsync(finalToken);
                var finalRoute = await selectedRoute.ValidateAsync(finalToken);
                var finalAssignment = await assignment.ValidateAsync(finalToken);
                finalSupport = await support.ValidateAsync(finalToken);
                var finalDefinitions = await definitions.ValidateAsync(finalToken);
                RequireSources(finalPrincipal, finalRoute, finalAssignment, finalDefinitions);
            }, now =>
            {
                RequireTime(finalSupport
                    ?? throw new InvalidOperationException("Event team terminal sources were not read."), now);
                prepared.Ended = true;
            });
        };
        return prepared;

        Principal BuildPrincipal(HumanPrincipalSourceFacts principal, HumanEventAssignmentSourceFacts facts, DateTime at)
        {
            var result = CerbosPrincipalBuilder.BuildPrincipalFromSources(principal);
            HumanEventAssignmentProjection.EnrichPrincipal(result, facts, at, providerNow);
            return result;
        }

        object Principal(
            HumanPrincipalSourceFacts principal, HumanEventAssignmentSourceFacts facts, DateTime at) =>
            CerbosPrincipalBuilder.CapturePrincipal(BuildPrincipal(principal, facts, at));

        void Custody()
        {
            if (prepared.Rejected || machine.IsMachineCaller || !ReferenceEquals(humanScope.Subject, subject)
                || !ReferenceEquals(httpContext.HttpContext, context) || !ReferenceEquals(context.User, authenticated)
                || tenantContext.TenantId != tenant)
                throw new UnauthorizedAccessException("Original Event team execution custody changed.");
        }

        void RequireSources(HumanPrincipalSourceFacts current, HumanAuthorizationRouteFacts currentRoute,
            HumanEventAssignmentSourceFacts currentAssignments, EventTeamPolicyFacts currentPolicy)
        {
            Custody();
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, current);
            if (current.SelectedTenantId != tenant || current.PrincipalId != subject.PrincipalId
                || currentRoute.SelectedTenantId != tenant || currentRoute.PrincipalId != subject.PrincipalId
                || currentAssignments.TenantId != tenant || currentAssignments.PrincipalId != subject.PrincipalId
                || currentAssignments.EventIds is not [var selected] || selected != eventId
                || resource.TenantId != tenant || resource.EventId != eventId
                || !route.IsSemanticallyEquivalentTo(Route(currentRoute, current)))
                throw new UnauthorizedAccessException("Original Event team selected sources changed.");
            if (!Same(original.Users, current.Users) || !Same(original.Bindings, current.Bindings)
                || !Same(original.PlatformGrants, current.PlatformGrants) || !Same(original.Roles, current.Roles)
                || !Same(original.TenantMemberships, current.TenantMemberships)
                || !Same(original.TenantGrants, current.TenantGrants) || !Same(original.Tenants, current.Tenants)
                || !Same(original.Organizations, current.Organizations) || !Same(original.Groups, current.Groups)
                || !Same(original.Permissions, current.Permissions)
                || original.PermissionMappingsExist != current.PermissionMappingsExist
                || !original.EventCreateOrganizations.SequenceEqual(current.EventCreateOrganizations)
                || !original.EventCreateGroups.SequenceEqual(current.EventCreateGroups)
                || !original.EventFinanceOrganizations.SequenceEqual(current.EventFinanceOrganizations)
                || !original.EventFinanceGroups.SequenceEqual(current.EventFinanceGroups))
                throw new UnauthorizedAccessException("An original Event team principal contributor changed.");
            if (!originalProviderPrincipal.Equals(Principal(current, assignments, evaluation)))
                throw new UnauthorizedAccessException("Original selected-provider principal ordering changed.");
            if (!Same(assignments.Events, currentAssignments.Events) || !Same(assignments.Actors, currentAssignments.Actors)
                || !Same(assignments.TenantMembers, currentAssignments.TenantMembers)
                || !Same(assignments.Organizations, currentAssignments.Organizations)
                || !Same(assignments.Groups, currentAssignments.Groups)
                || !Same(assignments.ProvenanceTypes, currentAssignments.ProvenanceTypes)
                || !Same(policy.Roles, currentPolicy.Roles) || !Same(policy.Permissions, currentPolicy.Permissions)
                || !Same(policy.RolePermissions, currentPolicy.RolePermissions)
                || policy.ResolvedUserId != currentPolicy.ResolvedUserId)
                throw new UnauthorizedAccessException("An original Event team ceiling or target contributor changed.");
            var expected = prepared.ExpectedAssignments();
            if (!Same(expected.Select(Normalize), currentPolicy.Assignments.Select(Normalize)))
                throw new UnauthorizedAccessException("Event team writes differ from their exact original transition.");
            var humanRows = expected.Where(row => row.UserId == subject.PrincipalId).Select(row =>
                new HumanEventAssignmentFact(row.Id, row.TenantId, row.EventId, row.UserId, row.RoleId, row.Status,
                    Canonical(row.StartsAtUtc), CanonicalNullable(row.ExpiresAtUtc), CanonicalNullable(row.RevokedAtUtc), row.Version));
            if (!Same(humanRows, currentAssignments.Assignments.Select(row => row with
            {
                StartsAtUtc = Canonical(row.StartsAtUtc),
                ExpiresAtUtc = CanonicalNullable(row.ExpiresAtUtc),
                RevokedAtUtc = CanonicalNullable(row.RevokedAtUtc)
            })))
                throw new UnauthorizedAccessException("Event assignment source disagrees with the closed team image.");
            var roleIds = humanRows.Select(row => row.RoleId).Concat(assignments.Organizations.Select(row => row.RoleId))
                .Concat(assignments.Groups.Select(row => row.RoleId)).ToHashSet();
            if (!Same(policy.Roles.Where(row => roleIds.Contains(row.Id)).Select(row =>
                    new HumanAssignmentRoleFact(row.Id, row.MasterCode)), currentAssignments.Roles)
                || !Same(policy.RolePermissions.Where(row => roleIds.Contains(row.RoleId))
                    .Join(policy.Permissions, mapping => mapping.PermissionId, permission => permission.Id,
                        (mapping, permission) => new HumanAssignmentPermissionFact(mapping.RoleId, permission.Id,
                            permission.MasterCode, permission.IsActive)), currentAssignments.Permissions))
                throw new UnauthorizedAccessException("The held assignment and complete definition sources disagree.");
        }

        void RequireTime(HumanSupportAccessSourceFacts support, DateTimeOffset now)
        {
            Custody();
            if (!lifetime.IsValidAt(now))
                throw new UnauthorizedAccessException("The original Event team credential expired.");
            // A self-revocation may remove its grant; only the ORIGINAL image supplies
            // time authority. An extension or freshly acquired role cannot stretch it.
            if (!originalProviderPrincipal.Equals(Principal(original, assignments, now.UtcDateTime)))
                throw new UnauthorizedAccessException("Original Event assignment windows no longer support the decision.");
            RequireSupport(originalSupport, now);
            RequireSupport(support, now);
        }

        void RequireSupport(HumanSupportAccessSourceFacts support, DateTimeOffset now)
        {
            if (support.Forwarding != originalSupport.Forwarding
                || support.Forwarding.WasForwarded && !HumanSupportAccessProjection.Context(support, now).IsActive
                || HumanSupportAccessProjection.BoundaryDenialReason(support, now, question.ResourceKind, question.Action,
                    AuthorizationFactAttributeProjection.ToAttributes(question.Facts)) is not null)
                throw new UnauthorizedAccessException("Original support custody no longer permits Event team management.");
        }
    }

    private static bool LocalAllowed(HumanPrincipalSourceFacts principal, HumanEventAssignmentSourceFacts assignments,
        EventAuthorizationFacts resource, DateTime at)
    {
        // Original Local manage-team has no blanket platform/tenant-administrator shortcut.
        // Its organization fallback, creating-user ownership and same-event permission remain.
        Guid? organization = resource.OrganizationId ?? (resource.GroupId is null ? resource.EventId : null);
        var member = organization.HasValue ? principal.Organizations.FirstOrDefault(row =>
            row.TenantId == resource.TenantId && row.OwnerId == organization
            && !row.IsDeleted && !row.PlacementIsDeleted && !row.OwnerIsDeleted) : null;
        bool orgAdmin = member?.RoleId == (int)RoleEnum.OrgAdmin;
        return orgAdmin || resource.UserId == principal.PrincipalId
            || HumanEventAssignmentProjection.Authority(assignments, at).Events[resource.EventId]
                .PermissionCodes.Contains(PermissionCodes.EventManageTeam);
    }

    private static bool Same<T>(IEnumerable<T> expected, IEnumerable<T> current) where T : notnull
    {
        var counts = expected.GroupBy(row => row).ToDictionary(group => group.Key, group => group.Count());
        var actual = current.GroupBy(row => row).ToArray();
        return counts.Count == actual.Length
            && actual.All(group => counts.TryGetValue(group.Key, out int count) && count == group.Count());
    }

    private static DateTime Canonical(DateTime value) => new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
    private static DateTime? CanonicalNullable(DateTime? value) => value is { } date ? Canonical(date) : null;
    private static EventTeamAssignmentFact Normalize(EventTeamAssignmentFact row) => row with
    {
        StartsAtUtc = Canonical(row.StartsAtUtc),
        ExpiresAtUtc = CanonicalNullable(row.ExpiresAtUtc),
        RevokedAtUtc = CanonicalNullable(row.RevokedAtUtc)
    };
}
