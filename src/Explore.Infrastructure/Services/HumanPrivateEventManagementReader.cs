using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;
using Explore.Domain;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Holds original principal, policy route, event assignments and support sources from the
/// idle provider decision through the physical read and its final disclosure boundary.
/// </summary>
public sealed class HumanPrivateEventManagementReader(
    IHumanCommitScopePreparation humanScope,
    IHttpContextAccessor contexts,
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
    Func<ISerializableAttemptOwner> resolveAttempt,
    Func<IHumanActionCommitFinalizer> resolveFinalizer,
    Func<IPrivateEventManagementRepository> resolveRepository,
    Func<IHumanEventTeamActionPreparation> resolveTeam) : IPrivateEventManagementReader, IPrivateEventManagementReadCustody
{
    private PrivateEventManagementRead? activeRequest;
    private Action? requireActive;

    public void RequireActive(PrivateEventManagementRead request)
    {
        if (!ReferenceEquals(activeRequest, request) || requireActive is null)
            throw new UnauthorizedAccessException("The repository read is not the exact admitted private operation.");
        requireActive();
    }

    public Task<PrivateEventManagementGraph> ReadAsync(PrivateEventManagementRead request, CancellationToken cancellationToken)
    {
        if (request.Kind is PrivateEventManagementReadKind.AuthorizationTarget or PrivateEventManagementReadKind.SessionAuthorizationTarget
            or PrivateEventManagementReadKind.Team)
            throw new UnauthorizedAccessException("Authorization sources have a separate closed entrypoint.");
        return ReadCoreAsync(request, cancellationToken);
    }

    public async Task<PrivateEventManagementGraph> ReadTeamAsync(
        GetEventTeamListRequest request, CancellationToken cancellationToken)
    {
        // Reuse the exact original Local/Cerbos manage-team decision, complete role
        // definitions and closed assignment image, not the generic management-read policy.
        var prepared = await resolveTeam().PrepareAsync(request, cancellationToken);
        return await resolveAttempt().ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, token);
            if (activeRequest is not null)
                throw new UnauthorizedAccessException("A team read cannot replace an active private operation.");
            var selection = new PrivateEventManagementRead(PrivateEventManagementReadKind.Team, request.EventId);
            activeRequest = selection;
            requireActive = () => prepared.RequireTeamRead(request);
            try
            {
                var graph = await resolveRepository().ReadAsync(selection, token);
                graph.TeamAuthority = prepared.GetTeamAuthority();
                graph.TeamPresets.AddRange(prepared.GetAssignablePresets());
                return graph;
            }
            finally
            {
                activeRequest = null;
                requireActive = null;
            }
        }, cancellationToken);
    }

    public async Task<PrivateEventManagementGraph> ReadSessionHistoryAsync(
        Guid sessionId, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("A session identifier is required.", nameof(sessionId));
        var target = await ReadCoreAsync(new(PrivateEventManagementReadKind.SessionAuthorizationTarget, SessionId: sessionId),
            cancellationToken);
        if (target.Events.SingleOrDefault() is not { } parent)
            throw new AuthorizationException(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement);
        return await ReadCoreAsync(new(PrivateEventManagementReadKind.SessionSyncHistory, parent.Id, sessionId,
            pageNumber, pageSize), cancellationToken);
    }

    public async Task<EventAuthoritySnapshot> ReadAuthorityAsync(Guid tenantId, Guid userId,
        IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken)
    {
        if (machine.IsMachineCaller || tenantId == Guid.Empty || tenantContext.TenantId != tenantId)
            throw new UnauthorizedAccessException("Assignment reads require the original tenant human.");
        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject ?? throw new UnauthorizedAccessException("Original human required.");
        var http = contexts.HttpContext ?? throw new UnauthorizedAccessException("Original request required.");
        var authenticated = http.User;
        var lifetime = await lifetimeObserver.ObserveAsync(authenticated, cancellationToken)
            ?? throw new UnauthorizedAccessException("Authentic credential lifetime required.");
        if (subject.PrincipalId != userId || subject.AccountKey != lifetime.AccountKey)
            throw new UnauthorizedAccessException("Assignment reads cannot choose another principal.");
        var source = resolvePrincipal();
        var assignments = resolveAssignments().Create(tenantId, eventIds);
        var exclusionSource = resolveExclusion();
        var original = await source.ObserveAsync(cancellationToken);
        var originalAssignments = await assignments.ObserveAsync(cancellationToken);
        DateTime at = clock.GetUtcNow().UtcDateTime;
        var snapshot = HumanEventAssignmentProjection.Authority(originalAssignments, at);
        Validate(original, originalAssignments);
        return await resolveAttempt().ExecuteAsync(async token =>
        {
            var exclusion = await exclusionSource.AcquireAsync(token);
            var heldPrincipal = await source.AcquireAsync(exclusion, token);
            var heldAssignments = await assignments.AcquireAsync(exclusion, token);
            Validate(heldPrincipal.Facts, heldAssignments.Facts);
            resolveFinalizer().Register(async finalToken =>
            {
                await exclusion.ValidateAsync(finalToken);
                Validate(await heldPrincipal.ValidateAsync(finalToken), await heldAssignments.ValidateAsync(finalToken));
            }, now =>
            {
                Custody(now);
                if (!Same(snapshot, HumanEventAssignmentProjection.Authority(originalAssignments, now.UtcDateTime)))
                    throw new UnauthorizedAccessException("Original event assignment lifetime ended.");
            });
            return snapshot;
        }, cancellationToken);

        void Custody(DateTimeOffset now)
        {
            if (machine.IsMachineCaller || !ReferenceEquals(humanScope.Subject, subject)
                || !ReferenceEquals(contexts.HttpContext, http) || !ReferenceEquals(http.User, authenticated)
                || tenantContext.TenantId != tenantId || !lifetime.IsValidAt(now))
                throw new UnauthorizedAccessException("Original assignment read custody changed.");
        }
        void Validate(HumanPrincipalSourceFacts principal, HumanEventAssignmentSourceFacts current)
        {
            Custody(clock.GetUtcNow());
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, principal);
            if (!Same(original, principal) || !Same(originalAssignments, current))
                throw new UnauthorizedAccessException("Original assignment sources changed.");
        }
    }

    public async Task<IReadOnlyList<Event>> ReadAuthorizationTargetsAsync(
        IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken)
    {
        if (eventIds.Count > IEventRepository.MaximumAuthorizationTargetBatchSize || eventIds.Any(id => id == Guid.Empty))
            throw new ArgumentException("Authorization target identifiers must be bounded and nonempty.", nameof(eventIds));
        var result = new List<Event>();
        foreach (Guid id in eventIds.Distinct())
            result.AddRange((await ReadCoreAsync(new(PrivateEventManagementReadKind.AuthorizationTarget, id),
                cancellationToken)).Events);
        return result.AsReadOnly();
    }

    private async Task<PrivateEventManagementGraph> ReadCoreAsync(
        PrivateEventManagementRead request, CancellationToken cancellationToken)
    {
        if (machine.IsMachineCaller || tenantContext.TenantId == Guid.Empty)
            throw new UnauthorizedAccessException("Private management reads require the original tenant human.");
        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject ?? throw new UnauthorizedAccessException("Original human required.");
        var http = contexts.HttpContext ?? throw new UnauthorizedAccessException("Original request required.");
        var authenticated = http.User;
        var lifetime = await lifetimeObserver.ObserveAsync(authenticated, cancellationToken)
            ?? throw new UnauthorizedAccessException("Authentic credential lifetime required.");
        Guid tenant = tenantContext.TenantId;
        if (subject.AccountKey != lifetime.AccountKey || request.ExpectedUserId is { } expected && expected != subject.PrincipalId)
            throw new UnauthorizedAccessException("The selected read cannot substitute another human.");
        bool self = request.Kind is PrivateEventManagementReadKind.MyEvents
            or PrivateEventManagementReadKind.Templates or PrivateEventManagementReadKind.SessionTemplates
            or PrivateEventManagementReadKind.SessionAuthorizationTarget;
        if (!self && request.EventId is not { } || request.EventId == Guid.Empty
            || !Enum.IsDefined(request.Kind) || request.PageNumber < 1 || request.PageSize is < 1 or > 100)
            throw new ArgumentException("A private read requires its exact target and bounded page.");
        var principalSource = resolvePrincipal();
        var routeSource = resolveRoute();
        var assignmentSource = self ? null : resolveAssignments().Create(tenant, [request.EventId!.Value]);
        var supportSource = await resolveSupport().CreateAsync(cancellationToken);
        var exclusionSource = resolveExclusion();
        var principal = await principalSource.ObserveAsync(cancellationToken);
        var routeFacts = await routeSource.ObserveAsync(cancellationToken);
        var support = await supportSource.ObserveAsync(cancellationToken);
        var assignments = assignmentSource is null ? null : await assignmentSource.ObserveAsync(cancellationToken);
        DateTime evaluatedAt = clock.GetUtcNow().UtcDateTime;
        var resource = assignments is null ? null : HumanEventAssignmentProjection.Resource(assignments, request.EventId!.Value);
        var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
        HumanAuthorizationRoute Route(HumanAuthorizationRouteFacts facts, HumanPrincipalSourceFacts human) =>
            HumanAuthorizationRouteProjector.Project(facts, deployment, false,
                lifetime.LocalSession is null || human.LocalSession is not null);
        var route = Route(routeFacts, principal);
        string action = request.Kind switch
        {
            PrivateEventManagementReadKind.Readiness => AuthorizationActions.Update,
            PrivateEventManagementReadKind.Registrations => AuthorizationActions.Events.ManageRegistrations,
            _ => AuthorizationActions.Events.ViewManagement
        };
        var question = resource is null ? null : new AuthorizationRequest(ResourceKinds.Event,
            resource.EventId.ToString("D"), action, Facts: resource);
        object ProviderPrincipal(HumanPrincipalSourceFacts human, HumanEventAssignmentSourceFacts? events, DateTime at) =>
            CerbosPrincipalBuilder.CapturePrincipal(BuildPrincipal(human, events, at));
        Cerbos.Sdk.Builder.Principal BuildPrincipal(HumanPrincipalSourceFacts human,
            HumanEventAssignmentSourceFacts? events, DateTime at)
        {
            var result = CerbosPrincipalBuilder.BuildPrincipalFromSources(human);
            if (events is not null) HumanEventAssignmentProjection.EnrichPrincipal(result, events, at, evaluatedAt);
            return result;
        }
        var originalProviderPrincipal = ProviderPrincipal(principal, assignments, evaluatedAt);
        RequireSources(principal, routeFacts, assignments);
        RequireTime(support, clock.GetUtcNow());
        if (!self && request.Kind != PrivateEventManagementReadKind.AuthorizationTarget)
        {
            if (question is null) throw new AuthorizationException(ResourceKinds.Event, action);
            bool allowed;
            if (route.Provider == HumanAuthorizationRouteProvider.Local)
                allowed = FallbackAuthorizationService.EvaluatePrivateEventManagementRead(principal, assignments!,
                    question, evaluatedAt);
            else if (route.Provider is HumanAuthorizationRouteProvider.CerbosInstance or HumanAuthorizationRouteProvider.CerbosTenant)
            {
                var decision = await cerbos.AuthorizePreparedHumanAsync(BuildPrincipal(principal, assignments, evaluatedAt),
                    question, tenant, deployment.UsePolicyScope, route.Endpoint!, cancellationToken);
                if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                    throw new AuthorizationProviderUnavailableException(question.ResourceKind, question.Action);
                allowed = decision.IsAllowed;
            }
            else allowed = false;
            if (!allowed) throw new AuthorizationException(question.ResourceKind, question.Action);
        }
        RequireTime(support, clock.GetUtcNow());
        return await resolveAttempt().ExecuteAsync(async token =>
        {
            var exclusion = await exclusionSource.AcquireAsync(token);
            var heldPrincipal = await principalSource.AcquireAsync(exclusion, token);
            var heldRoute = await routeSource.AcquireAsync(exclusion, token);
            var heldAssignments = assignmentSource is null ? null : await assignmentSource.AcquireAsync(exclusion, token);
            var heldSupport = await supportSource.AcquireAsync(exclusion, heldPrincipal, heldAssignments, token);
            RequireSources(heldPrincipal.Facts, heldRoute.Facts, heldAssignments?.Facts);
            RequireTime(heldSupport.Facts, clock.GetUtcNow());
            HumanSupportAccessSourceFacts? finalSupport = null;
            resolveFinalizer().Register(async finalToken =>
            {
                await exclusion.ValidateAsync(finalToken);
                RequireSources(await heldPrincipal.ValidateAsync(finalToken), await heldRoute.ValidateAsync(finalToken),
                    heldAssignments is null ? null : await heldAssignments.ValidateAsync(finalToken));
                finalSupport = await heldSupport.ValidateAsync(finalToken);
            }, now => RequireTime(finalSupport
                ?? throw new UnauthorizedAccessException("Private read final support sources are missing."), now));
            if (activeRequest is not null)
                throw new UnauthorizedAccessException("A private read cannot replace an active operation.");
            activeRequest = request;
            requireActive = () => RequireTime(heldSupport.Facts, clock.GetUtcNow());
            try
            {
                return await resolveRepository().ReadAsync(request, token);
            }
            finally
            {
                activeRequest = null;
                requireActive = null;
            }
        }, cancellationToken);

        void Custody()
        {
            if (machine.IsMachineCaller || !ReferenceEquals(humanScope.Subject, subject)
                || !ReferenceEquals(contexts.HttpContext, http) || !ReferenceEquals(http.User, authenticated)
                || tenantContext.TenantId != tenant)
                throw new UnauthorizedAccessException("Original private read custody changed.");
        }
        void RequireSources(HumanPrincipalSourceFacts current, HumanAuthorizationRouteFacts currentRoute,
            HumanEventAssignmentSourceFacts? currentAssignments)
        {
            Custody();
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, current);
            if (current.PrincipalId != subject.PrincipalId || current.SelectedTenantId != tenant
                || !route.IsSemanticallyEquivalentTo(Route(currentRoute, current))
                || !Same(principal, current) || !Same(routeFacts, currentRoute) || !Same(assignments, currentAssignments)
                || !originalProviderPrincipal.Equals(ProviderPrincipal(current, currentAssignments, evaluatedAt)))
                throw new UnauthorizedAccessException("Original private read policy contributors changed.");
        }
        void RequireTime(HumanSupportAccessSourceFacts current, DateTimeOffset now)
        {
            Custody();
            if (!lifetime.IsValidAt(now) || !Same(support, current)
                || !originalProviderPrincipal.Equals(ProviderPrincipal(principal, assignments, now.UtcDateTime))
                || current.Forwarding.WasForwarded && !HumanSupportAccessProjection.Context(current, now).IsActive
                || question is not null && HumanSupportAccessProjection.BoundaryDenialReason(current, now,
                    question.ResourceKind, question.Action, AuthorizationFactAttributeProjection.ToAttributes(question.Facts)) is not null)
                throw new UnauthorizedAccessException("Original private read credential, assignment or support lifetime ended.");
        }
    }

    // The source records contain complete ordered candidate images, including negative candidates.
    // Comparing their value representation does not consult caches or manufacture fresh authority.
    private static bool Same<T>(T expected, T current) =>
        string.Equals(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(current), StringComparison.Ordinal);
}
