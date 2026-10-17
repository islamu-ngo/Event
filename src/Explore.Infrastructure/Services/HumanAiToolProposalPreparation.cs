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
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Evaluates ordinary registry proposals while the original owner is idle, then retains their
/// complete source custody through one proposed-only commit. Missing foreign moderation targets
/// belong to the dedicated moderation preparation, never this same-tenant assignment source.
/// </summary>
public sealed class HumanAiToolProposalPreparation(
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
    Func<IHumanEventAssignmentSourceFactory> resolveAssignments,
    Func<IHumanSupportAccessSourceFactory> resolveSupport,
    Func<IPolicySourceSetFence> resolveExclusion,
    Func<IHumanActionCommitFinalizer> resolveFinalizer) : IHumanAiToolProposalPreparation
{
    public async Task<IPreparedHumanAiToolProposal> PrepareAsync(
        ProposeAiToolActionCommand request, AiConversation candidate, CancellationToken cancellationToken)
    {
        var definition = registry.Definitions.FirstOrDefault(value =>
            string.Equals(value.Name, request.ToolName.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.Kind.ToString(), request.ToolName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (machine.IsMachineCaller || definition is not { ExposeToMcp: true, RequiredAuthorization: { } requirement }
            || !registry.ValidatePayload(definition.Kind, request.PayloadJson).Succeeded)
            throw new AuthorizationException("The registry does not authorize this ordinary proposal question.");

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

        var prepared = new Prepared(request, candidate, definition.Kind);
        // All source objects capture the original idle execution owner, including exclusion.
        var exclusionSource = resolveExclusion();
        var principalSource = resolvePrincipal();
        var routeSource = resolveRoute();
        var supportSource = await resolveSupport().CreateAsync(cancellationToken);
        var principal = await principalSource.ObserveAsync(cancellationToken);
        RequirePrincipal(principal);
        var routeFacts = await routeSource.ObserveAsync(cancellationToken);
        var supportFacts = await supportSource.ObserveAsync(cancellationToken);
        using var payload = JsonDocument.Parse(request.PayloadJson);
        Guid? targetId = null;
        Guid? targetStamp = null;
        IHumanEventAssignmentSourceFence? assignmentSource = null;
        HumanEventAssignmentSourceFacts? assignments = null;
        DateTime evaluatedAt = clock.GetUtcNow().UtcDateTime;
        if (TryGetGuid(payload.RootElement, "eventId", out Guid eventId))
        {
            var target = await events.GetAiProposalAuthorizationTargetByIdAsync(eventId, cancellationToken);
            if (target is null || target.IsDeleted || target.TenantId != selectedTenant || target.Id != eventId)
                throw new AuthorizationException(requirement.ResourceKind, requirement.Action);
            targetId = target.Id;
            targetStamp = target.ConcurrencyStamp;
            assignmentSource = resolveAssignments().Create(selectedTenant, [eventId]);
            evaluatedAt = clock.GetUtcNow().UtcDateTime;
            assignments = await assignmentSource.ObserveAsync(cancellationToken);
        }
        DateTime providerNow = clock.GetUtcNow().UtcDateTime;
        IAuthorizationFacts resource = Resource(assignments);
        var question = new AuthorizationRequest(
            AuthorizationCapabilityCatalog.Require(requirement.ResourceKind, requirement.Action),
            ResolveResourceId(requirement.ResourceKind, payload.RootElement, selectedTenant), Facts: resource);
        var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
        HumanAuthorizationRoute Route(HumanAuthorizationRouteFacts facts, HumanPrincipalSourceFacts human) =>
            HumanAuthorizationRouteProjector.Project(facts, deployment, false,
                lifetime.LocalSession is null || human.LocalSession is not null);
        var route = Route(routeFacts, principal);
        Cerbos.Sdk.Builder.Principal BuildPrincipal(
            HumanPrincipalSourceFacts human, HumanEventAssignmentSourceFacts? currentAssignments, DateTime at)
        {
            var result = CerbosPrincipalBuilder.BuildPrincipalFromSources(human);
            if (currentAssignments is not null)
                HumanEventAssignmentProjection.EnrichPrincipal(result, currentAssignments, at, providerNow);
            return result;
        }
        var originalProviderPrincipal = CerbosPrincipalBuilder.CapturePrincipal(BuildPrincipal(principal, assignments, evaluatedAt));
        RequireSupport(supportFacts, clock.GetUtcNow());
        bool allowed;
        if (route.Provider == HumanAuthorizationRouteProvider.Local)
            allowed = FallbackAuthorizationService.EvaluatePreparedAiToolProposal(principal, assignments, question, selectedTenant, evaluatedAt);
        else if (route.Provider is HumanAuthorizationRouteProvider.CerbosInstance or HumanAuthorizationRouteProvider.CerbosTenant)
        {
            var decision = await cerbos.AuthorizePreparedHumanAsync(BuildPrincipal(principal, assignments, evaluatedAt),
                question, selectedTenant, deployment.UsePolicyScope, route.Endpoint!, cancellationToken);
            if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                throw new AuthorizationProviderUnavailableException(question.ResourceKind, question.Action);
            allowed = decision.IsAllowed;
        }
        else allowed = false;
        if (!allowed) throw new AuthorizationException(question.ResourceKind, question.Action);
        Compare(principal, routeFacts, assignments, clock.GetUtcNow().UtcDateTime);
        RequireSupport(supportFacts, clock.GetUtcNow());

        prepared.Acquire = async token =>
        {
            Custody();
            var exclusion = await exclusionSource.AcquireAsync(token);
            var heldPrincipal = await principalSource.AcquireAsync(exclusion, token);
            var heldRoute = await routeSource.AcquireAsync(exclusion, token);
            var heldAssignments = assignmentSource is null ? null : await assignmentSource.AcquireAsync(exclusion, token);
            var heldSupport = await supportSource.AcquireAsync(exclusion, heldPrincipal, heldAssignments, token);
            Compare(heldPrincipal.Facts, heldRoute.Facts, heldAssignments?.Facts, evaluatedAt);
            DateTimeOffset acquiredAt = clock.GetUtcNow();
            Compare(principal, routeFacts, assignments, acquiredAt.UtcDateTime);
            Compare(heldPrincipal.Facts, heldRoute.Facts, heldAssignments?.Facts, acquiredAt.UtcDateTime);
            RequireSupport(supportFacts, acquiredAt);
            RequireSupport(heldSupport.Facts, acquiredAt);
            HumanPrincipalSourceFacts? finalPrincipal = null;
            HumanAuthorizationRouteFacts? finalRoute = null;
            HumanEventAssignmentSourceFacts? finalAssignments = null;
            HumanSupportAccessSourceFacts? finalSupport = null;
            resolveFinalizer().Register(async finalToken =>
            {
                await exclusion.ValidateAsync(finalToken);
                finalPrincipal = await heldPrincipal.ValidateAsync(finalToken);
                finalRoute = await heldRoute.ValidateAsync(finalToken);
                finalAssignments = heldAssignments is null ? null : await heldAssignments.ValidateAsync(finalToken);
                finalSupport = await heldSupport.ValidateAsync(finalToken);
                Compare(finalPrincipal, finalRoute, finalAssignments, evaluatedAt);
                prepared.RequireRetainedProposal();
            }, now =>
            {
                Compare(principal, routeFacts, assignments, now.UtcDateTime);
                Compare(finalPrincipal ?? throw new InvalidOperationException("Final principal sources are missing."),
                    finalRoute ?? throw new InvalidOperationException("Final route sources are missing."),
                    finalAssignments, now.UtcDateTime);
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
                || tenantContext.TenantId != selectedTenant
                || !ReferenceEquals(registry.FindDefinition(definition.Kind), definition)
                || !registry.ValidatePayload(definition.Kind, request.PayloadJson).Succeeded)
                throw new UnauthorizedAccessException("Original ordinary proposal custody changed.");
            prepared.RequireOriginalRequest();
        }

        void RequirePrincipal(HumanPrincipalSourceFacts current)
        {
            Custody();
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, current);
            if (current.SelectedTenantId != selectedTenant || current.PrincipalId != subject.PrincipalId)
                throw new UnauthorizedAccessException("The original selected human is required.");
        }

        IAuthorizationFacts Resource(HumanEventAssignmentSourceFacts? current)
        {
            if (targetId is { } id)
            {
                if (current is null || current.PrincipalId != subject.PrincipalId || current.TenantId != selectedTenant
                    || current.EventIds is not [var selected] || selected != id
                    || current.Events is not [var parent] || parent.Id != id || parent.TenantId != selectedTenant
                    || parent.IsDeleted || parent.ConcurrencyStamp != targetStamp)
                    throw new UnauthorizedAccessException("The exact original same-tenant target is required.");
            }
            else if (current is not null)
                throw new UnauthorizedAccessException("A targetless proposal cannot acquire Event assignments.");
            return requirement.ResourceKind == ResourceKinds.Event && requirement.Action == AuthorizationActions.Create
                ? new PreCreateAuthorizationFacts(selectedTenant)
                : targetId is { } trustedId
                    ? new EventScopedAuthorizationFacts(selectedTenant, trustedId)
                    : new TenantScopedAuthorizationFacts(selectedTenant);
        }

        void Compare(HumanPrincipalSourceFacts current, HumanAuthorizationRouteFacts currentRoute,
            HumanEventAssignmentSourceFacts? currentAssignments, DateTime at)
        {
            RequirePrincipal(current);
            if (currentRoute.SelectedTenantId != selectedTenant || currentRoute.PrincipalId != subject.PrincipalId
                || !route.IsSemanticallyEquivalentTo(Route(currentRoute, current))
                || !Same(routeFacts.Settings, currentRoute.Settings)
                || !resource.Equals(Resource(currentAssignments))
                || !SamePrincipal(principal, current) || !SameAssignments(assignments, currentAssignments)
                || !originalProviderPrincipal.Equals(CerbosPrincipalBuilder.CapturePrincipal(BuildPrincipal(current, currentAssignments, at)))
                || route.Provider == HumanAuthorizationRouteProvider.Local
                    && !FallbackAuthorizationService.EvaluatePreparedAiToolProposal(current, currentAssignments, question, selectedTenant, at))
                throw new UnauthorizedAccessException("Original ordinary proposal sources changed.");
        }

        void RequireSupport(HumanSupportAccessSourceFacts facts, DateTimeOffset now)
        {
            Custody();
            if (!lifetime.IsValidAt(now) || facts.Forwarding != supportFacts.Forwarding
                || !Same(supportFacts.Sessions, facts.Sessions) || !Same(supportFacts.Settings, facts.Settings)
                || facts.Forwarding.WasForwarded && !HumanSupportAccessProjection.Context(facts, now).IsActive
                || HumanSupportAccessProjection.BoundaryDenialReason(facts, now, question.ResourceKind, question.Action,
                    AuthorizationFactAttributeProjection.ToAttributes(question.Facts)) is not null)
                throw new UnauthorizedAccessException("Original credential or support custody does not permit this proposal.");
        }
    }

    private static bool SamePrincipal(HumanPrincipalSourceFacts expected, HumanPrincipalSourceFacts current) =>
        Same(expected.Users, current.Users) && Same(expected.Bindings, current.Bindings)
        && Same(expected.PlatformGrants, current.PlatformGrants) && Same(expected.Roles, current.Roles)
        && Same(expected.TenantMemberships, current.TenantMemberships) && Same(expected.TenantGrants, current.TenantGrants)
        && Same(expected.Tenants, current.Tenants) && Same(expected.Organizations, current.Organizations)
        && Same(expected.Groups, current.Groups) && Same(expected.Permissions, current.Permissions)
        && expected.PermissionMappingsExist == current.PermissionMappingsExist
        && expected.EventCreateOrganizations.SequenceEqual(current.EventCreateOrganizations)
        && expected.EventCreateGroups.SequenceEqual(current.EventCreateGroups)
        && expected.EventFinanceOrganizations.SequenceEqual(current.EventFinanceOrganizations)
        && expected.EventFinanceGroups.SequenceEqual(current.EventFinanceGroups)
        && (expected.LocalSession is null ? current.LocalSession is null
            : current.LocalSession is { } local && expected.LocalSession.CredentialState == local.CredentialState
                && expected.LocalSession.EmailDeliveryEnabled == local.EmailDeliveryEnabled
                && Same(expected.LocalSession.Accounts, local.Accounts)
                && Same(expected.LocalSession.Operations, local.Operations)
                && Same(expected.LocalSession.PersonalActors, local.PersonalActors));

    private static bool SameAssignments(HumanEventAssignmentSourceFacts? expected, HumanEventAssignmentSourceFacts? current) =>
        expected is null ? current is null : current is not null
            && expected.EventIds.SequenceEqual(current.EventIds) && Same(expected.Events, current.Events)
            && Same(expected.Assignments, current.Assignments) && Same(expected.Roles, current.Roles)
            && Same(expected.Permissions, current.Permissions) && Same(expected.Actors, current.Actors)
            && Same(expected.TenantMembers, current.TenantMembers) && Same(expected.Organizations, current.Organizations)
            && Same(expected.Groups, current.Groups) && Same(expected.ProvenanceTypes, current.ProvenanceTypes);

    private static bool Same<T>(IEnumerable<T> expected, IEnumerable<T> actual) where T : notnull
    {
        var counts = expected.GroupBy(row => row).ToDictionary(group => group.Key, group => group.Count());
        var current = actual.GroupBy(row => row).ToArray();
        return counts.Count == current.Length && current.All(group =>
            counts.TryGetValue(group.Key, out int count) && count == group.Count());
    }

    private static string ResolveResourceId(string resourceKind, JsonElement payload, Guid tenantId)
    {
        string[] fieldNames = resourceKind switch
        {
            ResourceKinds.Event => ["eventId"],
            ResourceKinds.EventSession => ["sessionId", "eventId"],
            ResourceKinds.EventSessionGroup => ["groupId", "eventId"],
            ResourceKinds.EventDay => ["dayId", "eventId"],
            ResourceKinds.EventAgendaItem => ["agendaItemId", "eventId"],
            ResourceKinds.CustomPropertyDefinition => ["definitionId", "eventId"],
            ResourceKinds.CustomPropertyValue => ["eventCustomPropertyDefinitionId", "definitionId", "eventId"],
            ResourceKinds.CustomPropertyTemplate => ["templateId", "sessionTemplateId", "eventTemplateId", "sessionId", "eventId"],
            _ => []
        };
        foreach (var field in fieldNames)
            if (TryGetGuid(payload, field, out Guid value)) return value.ToString();
        return tenantId.ToString();
    }

    private static bool TryGetGuid(JsonElement payload, string field, out Guid value)
    {
        value = Guid.Empty;
        return payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(field, out var property)
            && property.ValueKind == JsonValueKind.String && Guid.TryParse(property.GetString(), out value);
    }

    private sealed class Prepared(ProposeAiToolActionCommand request, AiConversation candidate, AiProposedActionKind kind)
        : IPreparedHumanAiToolProposal
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
                throw new UnauthorizedAccessException("An ordinary proposal belongs to one original request and attempt.");
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
                || proposal.CreatedAt != createdAt || proposal.Kind != kind
                || proposal.PayloadJson != content.PayloadJson.Trim() || proposal.Status != AiProposedActionStatus.Proposed
                || proposal.MessageId is not null || proposal.ConfirmedBy is not null || proposal.ConfirmedAt is not null
                || proposal.RejectedBy is not null || proposal.RejectedAt is not null || proposal.ResultResourceId is not null
                || proposal.FailureCode is not null || proposal.FailureMessage is not null)
                throw new UnauthorizedAccessException("The retained proposal differs from the original proposed-only graph.");
        }
    }
}
