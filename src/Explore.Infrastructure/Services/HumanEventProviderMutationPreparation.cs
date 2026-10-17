using Cerbos.Sdk.Builder;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.RegistrationProviders.Commands;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Prepares the exact human Event/manage-registration-channels question outside transactions.
/// The private capability compares original semantic inputs and authentic lifetimes; neither
/// acquisition nor final admission calls a PDP or rebuilds an ambient principal.
/// </summary>
public sealed class HumanEventProviderMutationPreparation(
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
    Func<IHumanEventAssignmentSourceFactory> resolveAssignments,
    Func<IHumanSupportAccessSourceFactory> resolveSupport,
    Func<IPolicySourceSetFence> resolveExclusion,
    Func<IHumanActionCommitFinalizer> resolveFinalizer,
    Func<ISerializableAttemptOwner> resolveOwner,
    Func<IHumanEventProviderSourceFactory> resolveBusiness) : IHumanEventProviderMutationPreparation
{
    public async Task<T> ExecutePreparedAsync<T>(ICommand<BaseCommandResponse<Guid>> command,
        IPreparedHumanEventProviderMutation prepared, Func<CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (prepared is not Prepared original || !original.IsIssuedFor(this, command))
            throw new UnauthorizedAccessException("The prepared Event action belongs to another issuer or command.");
        try
        {
            return await resolveOwner().ExecuteAsync(async token =>
            {
                try
                {
                    await original.AcquireAsync(command, token);
                    var result = await mutation(token);
                    if (result is BaseCommandResponse<Guid> { IsSuccess: false })
                        throw new RejectedMutation<T>(result);
                    return result;
                }
                catch (Exception exception) when (ContainsBorrowedReceipt(exception))
                {
                    throw new InvalidOperationException(
                        "A business callback cannot supply the original owner's rollback receipt.");
                }
            }, cancellationToken);
        }
        catch (RejectedMutation<T> rejection)
        {
            return rejection.Result;
        }
    }

    public async Task<T> ExecuteAsync<T>(ICommand<BaseCommandResponse<Guid>> command,
        Func<CancellationToken, Task<T>> mutation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prepared = await PrepareAsync(command, cancellationToken);
            try
            {
                return await resolveOwner().ExecuteAsync(async token =>
                {
                    try
                    {
                        await prepared.AcquireAsync(command, token);
                        var result = await mutation(token);
                        if (result is BaseCommandResponse<Guid> { IsSuccess: false })
                            throw new RejectedMutation<T>(result);
                        return result;
                    }
                    catch (Exception exception) when (ContainsBorrowedReceipt(exception))
                    {
                        // Only the actual outer owner can acknowledge its own rollback.
                        // A nested/business callback cannot certify a retry of this owner.
                        // Do not retain its inner provider exception: the owner classifies
                        // exception chains recursively and would certify that borrowed signal.
                        throw new InvalidOperationException(
                            "A business callback cannot supply the original owner's rollback receipt.");
                    }
                }, cancellationToken);
            }
            catch (HumanMutationAttemptRolledBackException) when (attempt < 4)
            {
                // Acknowledged rollback cleared this attempt. Observe and decide afresh.
            }
            catch (RejectedMutation<T> rejection)
            {
                return rejection.Result;
            }
        }
    }

    private sealed class RejectedMutation<T>(T result) : Exception
    {
        internal T Result { get; } = result;
    }

    private static bool ContainsBorrowedReceipt(Exception exception) =>
        exception is HumanMutationAttemptRolledBackException
        || exception is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsBorrowedReceipt)
        || exception.InnerException is { } inner && ContainsBorrowedReceipt(inner);

    public async Task<IPreparedHumanEventProviderMutation> PrepareAsync(
        ICommand<BaseCommandResponse<Guid>> command, CancellationToken cancellationToken = default)
    {
        var (tenantId, eventId) = Target(command);
        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("An authenticated original human is required.");
        var request = httpContext.HttpContext
            ?? throw new UnauthorizedAccessException("The original authenticated request is required.");
        var authenticatedPrincipal = request.User;
        var lifetime = await lifetimeObserver.ObserveAsync(authenticatedPrincipal, cancellationToken)
            ?? throw new UnauthorizedAccessException("Original credential lifetime evidence is required.");
        if (subject.AccountKey != lifetime.AccountKey || !lifetime.IsValidAt(clock.GetUtcNow()))
            throw new UnauthorizedAccessException("The original credential is no longer valid.");
        if (tenantContext.TenantId != tenantId)
            throw new UnauthorizedAccessException("Event management requires its original selected tenant.");

        // Construction happens only after the canonical human scope has selected its owner.
        var principalSource = resolvePrincipal();
        var routeSource = resolveRoute();
        var assignmentSource = resolveAssignments().Create(tenantId, [eventId]);
        var supportSource = await resolveSupport().CreateAsync(cancellationToken);
        var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
        var principalFacts = await principalSource.ObserveAsync(cancellationToken);
        var routeFacts = await routeSource.ObserveAsync(cancellationToken);
        var supportFacts = await supportSource.ObserveAsync(cancellationToken);
        // Preserve the ordinary assignment query's time BEFORE retrieval and the independent
        // Cerbos nowUtc sample AFTER retrieval. Never sort the original provider lists.
        DateTime assignmentEvaluationUtc = clock.GetUtcNow().UtcDateTime;
        var assignmentFacts = await assignmentSource.ObserveAsync(cancellationToken);
        DateTime providerNowUtc = clock.GetUtcNow().UtcDateTime;
        RequireFacts(subject, lifetime, tenantId, eventId, principalFacts, routeFacts, assignmentFacts);
        var resource = RequireResource(assignmentFacts, tenantId, eventId);
        var check = new AuthorizationRequest(ResourceKinds.Event, eventId.ToString("D"),
            AuthorizationActions.Events.ManageRegistrationChannels, Facts: resource);
        var route = HumanAuthorizationRouteProjector.Project(routeFacts, deployment, false,
            lifetime.LocalSession is null || principalFacts.LocalSession is not null);
        RequireSupport(supportFacts, clock.GetUtcNow(), check);
        var principal = BuildPrincipal(principalFacts, assignmentFacts, assignmentEvaluationUtc, providerNowUtc);
        var serialized = CerbosPrincipalBuilder.CapturePrincipal(principal);
        var local = LocalInputs(principalFacts, assignmentFacts, resource, assignmentEvaluationUtc);
        bool allowed;
        if (route.Provider == HumanAuthorizationRouteProvider.Local)
            allowed = local.Organizer || !local.TenantAdmin && local.RegistrationPermission;
        else if (route.Provider is HumanAuthorizationRouteProvider.CerbosInstance or HumanAuthorizationRouteProvider.CerbosTenant)
        {
            var decision = await cerbos.AuthorizePreparedHumanAsync(principal, check, tenantId,
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
            if (!ReferenceEquals(humanScope.Subject, subject) || tenantContext.TenantId != tenantId
                || !ReferenceEquals(httpContext.HttpContext, request)
                || !ReferenceEquals(request.User, authenticatedPrincipal))
                throw new UnauthorizedAccessException("The original authenticated execution custody has changed.");
        }

        RequireCustody();
        if (!lifetime.IsValidAt(clock.GetUtcNow()))
            throw new UnauthorizedAccessException("The original credential expired during preparation.");
        Guid eventStamp = assignmentFacts.Events.Single(row => row.Id == eventId).ConcurrencyStamp;
        var businessSource = resolveBusiness().Create(HumanEventProviderSourceTarget.From(
            (ISecureRequest)command, eventId));
        var businessFacts = await businessSource.ObserveAsync(cancellationToken);

        void Compare(HumanPrincipalSourceFacts currentPrincipal, HumanAuthorizationRouteFacts currentRoute,
            HumanEventAssignmentSourceFacts currentAssignments, DateTime evaluationUtc)
        {
            RequireCustody();
            RequireFacts(subject, lifetime, tenantId, eventId, currentPrincipal, currentRoute, currentAssignments);
            var currentResource = RequireResource(currentAssignments, tenantId, eventId);
            var projectedRoute = HumanAuthorizationRouteProjector.Project(currentRoute, deployment, false,
                lifetime.LocalSession is null || currentPrincipal.LocalSession is not null);
            if (resource != currentResource
                || currentAssignments.Events.Single(row => row.Id == eventId).ConcurrencyStamp != eventStamp
                || !route.IsSemanticallyEquivalentTo(projectedRoute))
                throw new UnauthorizedAccessException("The original Event target or selected route has changed.");
            bool matches = route.Provider == HumanAuthorizationRouteProvider.Local
                ? local == LocalInputs(currentPrincipal, currentAssignments, currentResource, evaluationUtc)
                : serialized.Equals(CerbosPrincipalBuilder.CapturePrincipal(
                    BuildPrincipal(currentPrincipal, currentAssignments, evaluationUtc, providerNowUtc)));
            if (!matches)
                throw new UnauthorizedAccessException("The original selected-provider inputs have changed.");
        }

        DateTimeOffset preparedAt = clock.GetUtcNow();
        if (!lifetime.IsValidAt(preparedAt))
            throw new UnauthorizedAccessException("The original credential expired during subordinate observation.");
        Compare(principalFacts, routeFacts, assignmentFacts, preparedAt.UtcDateTime);
        RequireSupport(supportFacts, preparedAt, check);
        return new Prepared(this, command, async token =>
        {
            RequireCustody();
            var exclusion = await resolveExclusion().AcquireAsync(token);
            var principalLease = await principalSource.AcquireAsync(exclusion, token);
            var routeLease = await routeSource.AcquireAsync(exclusion, token);
            var assignmentLease = await assignmentSource.AcquireAsync(exclusion, token);
            var supportLease = await supportSource.AcquireAsync(exclusion, principalLease, assignmentLease, token);
            var businessLease = await businessSource.AcquireAsync(businessFacts, exclusion, principalLease,
                routeLease, assignmentLease, supportLease, token);
            if (supportLease.Facts.Forwarding != supportFacts.Forwarding)
                throw new UnauthorizedAccessException("The original forwarded support selector changed.");
            Compare(principalLease.Facts, routeLease.Facts, assignmentLease.Facts, assignmentEvaluationUtc);
            DateTimeOffset acquiredAt = clock.GetUtcNow();
            if (!lifetime.IsValidAt(acquiredAt))
                throw new UnauthorizedAccessException("The original credential expired before acquisition.");
            Compare(principalFacts, routeFacts, assignmentFacts, acquiredAt.UtcDateTime);
            Compare(principalLease.Facts, routeLease.Facts, assignmentLease.Facts, acquiredAt.UtcDateTime);
            RequireSupport(supportFacts, acquiredAt, check);
            RequireSupport(supportLease.Facts, acquiredAt, check);
            FinalFacts? final = null;
            resolveFinalizer().Register(async finalToken =>
            {
                // All four calls can block. Do not sample lifetime/assignment time between them.
                var freshPrincipal = await principalLease.ValidateAsync(finalToken);
                var freshRoute = await routeLease.ValidateAsync(finalToken);
                var freshAssignments = await assignmentLease.ValidateAsync(finalToken);
                var freshSupport = await supportLease.ValidateAsync(finalToken);
                await businessLease.ValidateAsync(finalToken);
                Compare(freshPrincipal, freshRoute, freshAssignments, assignmentEvaluationUtc);
                final = new(freshPrincipal, freshRoute, freshAssignments, freshSupport);
            }, now =>
            {
                var fresh = final ?? throw new InvalidOperationException("Final source admission has not completed.");
                if (!lifetime.IsValidAt(now))
                    throw new UnauthorizedAccessException("The original credential expired before commit.");
                // Reproject at authentic FINAL time: expiry/start can change authority without
                // any database write. Retain the original provider nowUtc value, not a made-up
                // PDP decision at a later wall clock or a blanket raw-row invalidation rule.
                // Project BOTH snapshots: an own-write expiry extension cannot stretch the
                // original prepared grant, but expiry of an irrelevant candidate is harmless.
                Compare(principalFacts, routeFacts, assignmentFacts, now.UtcDateTime);
                Compare(fresh.Principal, fresh.Route, fresh.Assignments, now.UtcDateTime);
                RequireSupport(supportFacts, now, check);
                RequireSupport(fresh.Support, now, check);
            });
        }, async token =>
        {
            RequireCustody();
            var freshPrincipal = await principalSource.ObserveAsync(token);
            var freshRoute = await routeSource.ObserveAsync(token);
            var freshAssignments = await assignmentSource.ObserveAsync(token);
            var freshSupport = await supportSource.ObserveAsync(token);
            await businessSource.ValidateObservationAsync(businessFacts, token);
            if (freshSupport.Forwarding != supportFacts.Forwarding)
                throw new UnauthorizedAccessException("The original forwarded support selector changed.");
            Compare(freshPrincipal, freshRoute, freshAssignments, assignmentEvaluationUtc);
            DateTimeOffset now = clock.GetUtcNow();
            if (!lifetime.IsValidAt(now))
                throw new UnauthorizedAccessException("The original credential expired during remote preparation.");
            Compare(principalFacts, routeFacts, assignmentFacts, now.UtcDateTime);
            Compare(freshPrincipal, freshRoute, freshAssignments, now.UtcDateTime);
            RequireSupport(supportFacts, now, check);
            RequireSupport(freshSupport, now, check);
        });
    }

    private static (Guid TenantId, Guid EventId) Target(ICommand<BaseCommandResponse<Guid>> command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var target = command switch
        {
            PollRegistrationProviderReconciliationCommand value => (value.TenantId, value.EventId),
            QueueManualRegistrationProviderImportCommand value => (value.TenantId, value.EventId),
            RetryRegistrationProviderParkedItemCommand value => (value.TenantId, value.EventId),
            ResolveRegistrationProviderQueueItemCommand value => (value.TenantId, value.EventId),
            CreateRegistrationProviderBindingCommand value => (value.TenantId, value.EventId),
            UpdateRegistrationProviderBindingCommand value => (value.TenantId, value.EventId),
            DeleteRegistrationProviderBindingCommand value => (value.TenantId, value.EventId),
            PublishEventRegistrationProviderBindingCommand value => (value.TenantId, value.EventId),
            ReplaceEventDraftRegistrationProviderMappingsCommand value => (value.TenantId, value.EventId),
            ImportExternalRegistrationProviderFormVersionCommand value => (value.TenantId, value.EventId),
            UpsertRegistrationChannelCommand value => (value.TenantId, value.EventId),
            DeleteRegistrationChannelCommand value => (value.TenantId, value.EventId),
            _ => throw new ArgumentException("Only exact Event registration-provider management commands are supported.", nameof(command))
        };
        if (target.TenantId == Guid.Empty || target.EventId == Guid.Empty)
            throw new ArgumentException("An exact nonempty tenant and Event target is required.", nameof(command));
        return target;
    }

    private static void RequireFacts(HumanCommitSubject subject, HumanAuthenticationLifetime lifetime,
        Guid tenantId, Guid eventId, HumanPrincipalSourceFacts principal, HumanAuthorizationRouteFacts route,
        HumanEventAssignmentSourceFacts assignments)
    {
        HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, principal);
        if (principal.SelectedTenantId != tenantId || route.SelectedTenantId != tenantId
            || route.PrincipalId != subject.PrincipalId || assignments.PrincipalId != subject.PrincipalId
            || assignments.TenantId != tenantId || assignments.EventIds is not [var originalEvent] || originalEvent != eventId)
            throw new UnauthorizedAccessException("Sources do not belong to the original human, tenant and Event.");
    }

    private static EventAuthorizationFacts RequireResource(HumanEventAssignmentSourceFacts assignments,
        Guid tenantId, Guid eventId)
    {
        var resource = HumanEventAssignmentProjection.Resource(assignments, eventId);
        return resource is not null && resource.EventId == eventId && resource.TenantId == tenantId
            ? resource : throw new UnauthorizedAccessException("The original persisted Event target is unavailable.");
    }

    private static Principal BuildPrincipal(HumanPrincipalSourceFacts principal, HumanEventAssignmentSourceFacts assignments,
        DateTime assignmentEvaluationUtc, DateTime providerNowUtc)
    {
        var result = CerbosPrincipalBuilder.BuildPrincipalFromSources(principal);
        HumanEventAssignmentProjection.EnrichPrincipal(result, assignments, assignmentEvaluationUtc, providerNowUtc);
        return result;
    }

    private static (bool Organizer, bool TenantAdmin, bool RegistrationPermission) LocalInputs(
        HumanPrincipalSourceFacts principal, HumanEventAssignmentSourceFacts assignments,
        EventAuthorizationFacts resource, DateTime evaluationUtc)
    {
        // FallbackAuthorizationService.IsVerifiedOrganizerControllerAsync is intentionally NOT
        // ControlsOrganizer: it does not require actor suspension/placement eligibility checks.
        // HasPermissionInOrganization/Group uses the first visible membership and database code
        // comparison. Preserve those rules, including its original empty-mapping fallback.
        bool MemberCanCreate(IEnumerable<HumanPrincipalOwnerMembershipFact> members, Guid owner, RoleEnum fallback)
        {
            var member = members.FirstOrDefault(row => row.TenantId == resource.TenantId
                && row.OwnerId == owner && !row.IsDeleted && !row.PlacementIsDeleted);
            return member is not null && (principal.Permissions.Any(permission => permission.RoleId == member.RoleId
                && permission.IsActive && permission.MatchesEventCreateCode)
                || !principal.PermissionMappingsExist && member.RoleId == (int)fallback);
        }
        bool organizer = resource.OrganizerUserId is { } user ? user == principal.PrincipalId
            : resource.OrganizerOrganizationId is { } organization
                ? MemberCanCreate(principal.Organizations, organization, RoleEnum.OrgAdmin)
                : resource.OrganizerGroupId is { } group
                    && MemberCanCreate(principal.Groups, group, RoleEnum.GroupAdmin);
        if (organizer) return (true, false, false);
        // IsTenantAdminAsync does not erase tenant-admin status for instance administrators.
        // No instance administrator shortcut exists for this exact Event action.
        bool tenantAdmin = principal.TenantGrants.Any(grant => grant.TenantId == resource.TenantId
            && grant.RoleId == (int)RoleEnum.TenantAdmin && grant.RevokedAt is null
            && principal.TenantMemberships.Any(member => member.Id == grant.TenantUserId
                && member.StatusId == (int)TenantUserStatusEnum.Active && !member.IsDeleted));
        if (tenantAdmin) return (false, true, false);
        bool permission = HumanEventAssignmentProjection.Authority(assignments, evaluationUtc)
            .Events[resource.EventId].PermissionCodes.Contains(PermissionCodes.EventRegistrationManage);
        return (false, false, permission);
    }

    private static void RequireSupport(HumanSupportAccessSourceFacts facts, DateTimeOffset now, AuthorizationRequest check)
    {
        if (HumanSupportAccessProjection.BoundaryDenialReason(facts, now, check.ResourceKind, check.Action,
                AuthorizationFactAttributeProjection.ToAttributes(check.Facts)) is not null)
            throw new UnauthorizedAccessException("The original support boundary does not permit the Event action.");
    }

    private sealed record FinalFacts(HumanPrincipalSourceFacts Principal, HumanAuthorizationRouteFacts Route,
        HumanEventAssignmentSourceFacts Assignments, HumanSupportAccessSourceFacts Support);

    private sealed class Prepared(HumanEventProviderMutationPreparation issuer,
        ICommand<BaseCommandResponse<Guid>> command, Func<CancellationToken, Task> acquire,
        Func<CancellationToken, Task> validate) : IPreparedHumanEventProviderMutation
    {
        private int _consumed;

        internal bool IsIssuedFor(HumanEventProviderMutationPreparation originalIssuer,
            ICommand<BaseCommandResponse<Guid>> originalCommand) =>
            ReferenceEquals(issuer, originalIssuer) && ReferenceEquals(command, originalCommand);

        public Task ValidateAsync(ICommand<BaseCommandResponse<Guid>> originalCommand,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _consumed) != 0 || !ReferenceEquals(command, originalCommand))
                throw new UnauthorizedAccessException("Remote admission requires the unused original Event action.");
            cancellationToken.ThrowIfCancellationRequested();
            return validate(cancellationToken);
        }

        public Task AcquireAsync(ICommand<BaseCommandResponse<Guid>> originalCommand,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _consumed, 1) != 0)
                throw new InvalidOperationException("A prepared Event action belongs to one attempt only.");
            // Immutable command scalar targets cannot be replaced, even with an equal record.
            if (!ReferenceEquals(command, originalCommand))
                throw new UnauthorizedAccessException("The prepared Event action belongs to another command.");
            cancellationToken.ThrowIfCancellationRequested();
            return acquire(cancellationToken);
        }
    }
}
