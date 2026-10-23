using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;
using Explore.Domain.Enums;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Preserves the selected conversation tenant's provider and original credential while asking
/// one HeavyModerateEvent question about an internally resolved target. This issues no execution grant.
/// </summary>
public sealed class HumanAiModerationProposalPreparation(
    IHumanCommitScopePreparation humanScope,
    IHttpContextAccessor httpContext,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    IMachinePrincipalAccessor machine,
    ITenantContext tenantContext,
    IAiToolContractRegistry registry,
    IEventRepository events,
    TimeProvider clock,
    CerbosAuthorizationService cerbos,
    IOptions<AuthorizationProviderDeploymentOptions> authorizationOptions,
    IOptions<CerbosSettings> cerbosOptions,
    Func<IHumanPrincipalSourceFence> resolvePrincipal,
    Func<IHumanAuthorizationRouteSource> resolveRoute,
    Func<IHumanAiModerationProposalTargetSourceFactory> resolveTarget,
    Func<IHumanSupportAccessSourceFactory> resolveSupport,
    Func<IPolicySourceSetFence> resolveExclusion,
    Func<IHumanActionCommitFinalizer> resolveFinalizer) : IHumanAiModerationProposalPreparation
{
    public async Task<IPreparedHumanAiModerationProposal> PrepareAsync(
        ProposeAiToolActionCommand request, AiConversation candidate, CancellationToken cancellationToken)
    {
        var definition = registry.Definitions.FirstOrDefault(value =>
            string.Equals(value.Name, request.ToolName.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.Kind.ToString(), request.ToolName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (machine.IsMachineCaller || definition is not
            {
                Kind: AiProposedActionKind.HeavyModerateEvent, ExposeToMcp: true,
                RequiredAuthorization: { ResourceKind: ResourceKinds.Event, Action: AuthorizationActions.Events.ModerateHeavy }
            } || !registry.ValidatePayload(definition.Kind, request.PayloadJson).Succeeded)
            throw new AuthorizationException(ResourceKinds.Event, AuthorizationActions.Events.ModerateHeavy);
        using var payload = JsonDocument.Parse(request.PayloadJson);
        if (!payload.RootElement.TryGetProperty("eventId", out var eventProperty)
            || !eventProperty.TryGetGuid(out Guid eventId) || eventId == Guid.Empty
            || await events.GetAiProposalAuthorizationTargetByIdAsync(eventId, cancellationToken) is not null)
            throw new UnauthorizedAccessException("The dedicated source is only for an exact missing moderation target.");

        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject ?? throw new UnauthorizedAccessException("The original human is required.");
        var context = httpContext.HttpContext ?? throw new UnauthorizedAccessException("The original request is required.");
        var authenticated = context.User;
        var lifetime = await lifetimeObserver.ObserveAsync(authenticated, cancellationToken)
            ?? throw new UnauthorizedAccessException("Authentic credential lifetime is required.");
        Guid selectedTenant = candidate.TenantId;
        if (subject.AccountKey != lifetime.AccountKey || candidate.UserId != subject.PrincipalId
            || candidate.Id != request.ConversationId || candidate.IsDeleted || candidate.Status != AiConversationStatus.Active
            || selectedTenant == Guid.Empty || tenantContext.TenantId != selectedTenant)
            throw new UnauthorizedAccessException("The original conversation owner is required.");
        var prepared = new Prepared(request, candidate);
        var principalSource = resolvePrincipal();
        var principal = await principalSource.ObserveAsync(cancellationToken);
        RequireAdministrator(principal);
        // Discovery follows genuine persisted administrator/credential admission, never claims,
        // tenant administration or model-provided management-context booleans.
        RequireTime(clock.GetUtcNow());
        var routeSource = resolveRoute();
        var routeFacts = await routeSource.ObserveAsync(cancellationToken);
        var supportSource = await resolveSupport().CreateAsync(cancellationToken);
        var supportFacts = await supportSource.ObserveAsync(cancellationToken);
        var targetSource = resolveTarget().Create(eventId);
        DateTime evaluatedAt = clock.GetUtcNow().UtcDateTime;
        var target = await targetSource.ObserveAsync(cancellationToken)
            ?? throw new AuthorizationException(ResourceKinds.Event, AuthorizationActions.Events.ModerateHeavy);
        DateTime providerNow = clock.GetUtcNow().UtcDateTime;
        var resource = Resource(target);
        var question = new AuthorizationRequest(ResourceKinds.Event, eventId.ToString("D"),
            AuthorizationActions.Events.ModerateHeavy, Facts: resource);
        var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
        HumanAuthorizationRoute Route(HumanAuthorizationRouteFacts facts, HumanPrincipalSourceFacts human) =>
            HumanAuthorizationRouteProjector.Project(facts, deployment, false,
                lifetime.LocalSession is null || human.LocalSession is not null);
        var route = Route(routeFacts, principal);
        Cerbos.Sdk.Builder.Principal BuildPrincipal(HumanPrincipalSourceFacts human, HumanEventAssignmentSourceFacts assignments, DateTime at)
        {
            var result = CerbosPrincipalBuilder.BuildPrincipalFromSources(human);
            HumanEventAssignmentProjection.EnrichPrincipal(result, assignments, at, providerNow);
            return result;
        }
        var originalProviderPrincipal = CerbosPrincipalBuilder.CapturePrincipal(BuildPrincipal(principal, target, evaluatedAt));
        RequireSupport(supportFacts, clock.GetUtcNow());
        bool allowed;
        if (route.Provider == HumanAuthorizationRouteProvider.Local)
            allowed = FallbackAuthorizationService.EvaluatePreparedAiModerationProposal(principal, question, selectedTenant);
        else if (route.Provider is HumanAuthorizationRouteProvider.CerbosInstance or HumanAuthorizationRouteProvider.CerbosTenant)
        {
            var decision = await cerbos.AuthorizePreparedHumanAsync(BuildPrincipal(principal, target, evaluatedAt),
                question, selectedTenant, deployment.UsePolicyScope, route.Endpoint!, cancellationToken);
            if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                throw new AuthorizationProviderUnavailableException(question.ResourceKind, question.Action);
            allowed = decision.IsAllowed;
        }
        else allowed = false;
        if (!allowed) throw new AuthorizationException(question.ResourceKind, question.Action);
        Compare(principal, routeFacts, target, clock.GetUtcNow().UtcDateTime);
        RequireSupport(supportFacts, clock.GetUtcNow());
        var exclusionSource = resolveExclusion();

        prepared.Acquire = async token =>
        {
            Custody();
            var exclusion = await exclusionSource.AcquireAsync(token);
            var heldPrincipal = await principalSource.AcquireAsync(exclusion, token);
            RequireAdministrator(heldPrincipal.Facts);
            var heldRoute = await routeSource.AcquireAsync(exclusion, token);
            var heldTarget = await targetSource.AcquireAsync(exclusion, heldPrincipal, token);
            var heldSupport = await supportSource.AcquireAsync(exclusion, heldPrincipal, heldTarget, token);
            Compare(heldPrincipal.Facts, heldRoute.Facts, heldTarget.Facts, evaluatedAt);
            RequireTime(clock.GetUtcNow());
            RequireSupport(heldSupport.Facts, clock.GetUtcNow());
            HumanPrincipalSourceFacts? finalPrincipal = null;
            HumanAuthorizationRouteFacts? finalRoute = null;
            HumanEventAssignmentSourceFacts? finalTarget = null;
            HumanSupportAccessSourceFacts? finalSupport = null;
            resolveFinalizer().Register(async finalToken =>
            {
                await exclusion.ValidateAsync(finalToken);
                finalPrincipal = await heldPrincipal.ValidateAsync(finalToken);
                finalRoute = await heldRoute.ValidateAsync(finalToken);
                finalTarget = await heldTarget.ValidateAsync(finalToken);
                finalSupport = await heldSupport.ValidateAsync(finalToken);
                Compare(finalPrincipal, finalRoute, finalTarget, evaluatedAt);
                prepared.RequireRetainedProposal();
            }, now =>
            {
                RequireTime(now);
                Compare(principal, routeFacts, target, now.UtcDateTime);
                Compare(finalPrincipal ?? throw new InvalidOperationException("Final principal sources are missing."),
                    finalRoute ?? throw new InvalidOperationException("Final route sources are missing."),
                    finalTarget ?? throw new InvalidOperationException("Final target sources are missing."), now.UtcDateTime);
                RequireSupport(supportFacts, now);
                RequireSupport(finalSupport ?? throw new InvalidOperationException("Final support sources are missing."), now);
                prepared.RequireRetainedProposal();
                prepared.Ended = true;
            });
        };
        return prepared;

        void Custody()
        {
            if (prepared.Rejected || machine.IsMachineCaller || !ReferenceEquals(humanScope.Subject, subject)
                || !ReferenceEquals(httpContext.HttpContext, context) || !ReferenceEquals(context.User, authenticated)
                || tenantContext.TenantId != selectedTenant)
                throw new UnauthorizedAccessException("Original moderation proposal custody changed.");
            prepared.RequireOriginalRequest();
        }

        void RequireAdministrator(HumanPrincipalSourceFacts current)
        {
            Custody();
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, current);
            if (current.SelectedTenantId != selectedTenant || current.PrincipalId != subject.PrincipalId
                || !current.PlatformGrants.Any(grant => current.Roles.Any(role => role.Id == grant.RoleId
                    && role.RoleScopeId == (int)RoleScopeEnum.Platform && role.MatchesPlatformAdministratorCode)))
                throw new UnauthorizedAccessException("Current persisted instance administration is required.");
        }

        EventScopedAuthorizationFacts Resource(HumanEventAssignmentSourceFacts facts)
        {
            if (facts.PrincipalId != subject.PrincipalId || facts.TenantId == selectedTenant
                || facts.EventIds is not [var selected] || selected != eventId
                || facts.Events is not [var parent] || parent.Id != eventId || parent.TenantId != facts.TenantId || parent.IsDeleted)
                throw new UnauthorizedAccessException("The exact persisted cross-tenant moderation target is required.");
            return new(facts.TenantId, eventId);
        }

        void Compare(HumanPrincipalSourceFacts current, HumanAuthorizationRouteFacts currentRoute,
            HumanEventAssignmentSourceFacts currentTarget, DateTime at)
        {
            RequireAdministrator(current);
            if (currentRoute.SelectedTenantId != selectedTenant || currentRoute.PrincipalId != subject.PrincipalId
                || !route.IsSemanticallyEquivalentTo(Route(currentRoute, current))
                || resource != Resource(currentTarget)
                || !Same(principal.PlatformGrants, current.PlatformGrants) || !Same(principal.Bindings, current.Bindings)
                || !Same(target.Events, currentTarget.Events) || !Same(target.Assignments, currentTarget.Assignments)
                || !Same(target.Roles, currentTarget.Roles) || !Same(target.Permissions, currentTarget.Permissions)
                || !Same(target.Actors, currentTarget.Actors) || !Same(target.TenantMembers, currentTarget.TenantMembers)
                || !Same(target.Organizations, currentTarget.Organizations) || !Same(target.Groups, currentTarget.Groups)
                || !Same(target.ProvenanceTypes, currentTarget.ProvenanceTypes)
                || !originalProviderPrincipal.Equals(CerbosPrincipalBuilder.CapturePrincipal(BuildPrincipal(current, currentTarget, at))))
                throw new UnauthorizedAccessException("Original moderation proposal sources changed.");
        }

        void RequireTime(DateTimeOffset now)
        {
            Custody();
            if (!lifetime.IsValidAt(now))
                throw new UnauthorizedAccessException("The original moderation proposal credential expired.");
        }

        void RequireSupport(HumanSupportAccessSourceFacts facts, DateTimeOffset now)
        {
            RequireTime(now);
            if (facts.Forwarding != supportFacts.Forwarding
                || facts.Forwarding.WasForwarded && !HumanSupportAccessProjection.Context(facts, now).IsActive
                || HumanSupportAccessProjection.BoundaryDenialReason(facts, now, question.ResourceKind, question.Action,
                    AuthorizationFactAttributeProjection.ToAttributes(question.Facts)) is not null)
                throw new UnauthorizedAccessException("Original support custody does not permit this moderation proposal.");
        }
    }

    private static bool Same<T>(IEnumerable<T> expected, IEnumerable<T> actual) where T : notnull
    {
        var counts = expected.GroupBy(row => row).ToDictionary(group => group.Key, group => group.Count());
        var current = actual.GroupBy(row => row).ToArray();
        return counts.Count == current.Length && current.All(group =>
            counts.TryGetValue(group.Key, out int count) && count == group.Count());
    }

    private sealed class Prepared(ProposeAiToolActionCommand request, AiConversation candidate) : IPreparedHumanAiModerationProposal
    {
        private readonly ProposeAiToolActionCommand content = request with { };
        private readonly Guid tenant = candidate.TenantId;
        private readonly Guid user = candidate.UserId;
        private readonly Guid? actor = candidate.ActorId;
        private readonly Guid stamp = candidate.ConcurrencyStamp;
        private AiConversation? graph;
        private AiProposedAction? proposal;
        private Guid proposalId;
        private DateTime createdAt;
        private int consumed;
        private bool acquired;
        internal bool Rejected { get; private set; }
        internal bool Ended { get; set; }
        internal Func<CancellationToken, Task> Acquire { private get; set; } = null!;

        internal void RequireOriginalRequest()
        {
            if (request != content || Ended || Rejected)
                throw new UnauthorizedAccessException("The original proposal command or lifetime changed.");
        }

        public async Task AcquireAsync(ProposeAiToolActionCommand originalRequest, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref consumed, 1) != 0 || !ReferenceEquals(request, originalRequest))
            {
                Rejected = true;
                throw new UnauthorizedAccessException("A moderation proposal belongs to one original request and attempt.");
            }
            try
            {
                RequireOriginalRequest();
                await Acquire(cancellationToken);
                acquired = true;
            }
            catch { Rejected = true; throw; }
        }

        public void RetainProposal(AiConversation conversation, AiProposedAction proposedAction)
        {
            RequireOriginalRequest();
            if (!acquired || graph is not null || conversation.ConcurrencyStamp != stamp)
                throw new UnauthorizedAccessException("The original acquired conversation is required.");
            graph = conversation;
            proposal = proposedAction;
            proposalId = proposedAction.Id;
            createdAt = proposedAction.CreatedAt;
            RequireRetainedProposal();
        }

        internal void RequireRetainedProposal()
        {
            RequireOriginalRequest();
            if (!acquired || graph is null || proposal is null || graph.Id != content.ConversationId
                || graph.TenantId != tenant || graph.UserId != user || graph.ActorId != actor
                || graph.IsDeleted || graph.Status != AiConversationStatus.Active
                || !graph.ProposedActions.Any(value => ReferenceEquals(value, proposal))
                || proposal.Id != proposalId || proposalId == Guid.Empty || proposal.TenantId != tenant
                || proposal.ConversationId != graph.Id || proposal.ActingActorId != actor || proposal.CreatedBy != user
                || proposal.CreatedAt != createdAt || proposal.Kind != AiProposedActionKind.HeavyModerateEvent
                || proposal.PayloadJson != content.PayloadJson.Trim() || proposal.Status != AiProposedActionStatus.Proposed
                || proposal.MessageId is not null || proposal.ConfirmedBy is not null || proposal.ConfirmedAt is not null
                || proposal.RejectedBy is not null || proposal.RejectedAt is not null || proposal.ResultResourceId is not null
                || proposal.FailureCode is not null || proposal.FailureMessage is not null)
                throw new UnauthorizedAccessException("The retained proposal differs from the original proposed-only graph.");
        }
    }
}
