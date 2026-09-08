namespace Explore.API.Hateoas;

using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Hateoas;
using Explore.Domain;
using Explore.Domain.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

public sealed class HateoasAuthorizationEvaluator : IHateoasAuthorizationEvaluator
{
    private static readonly ActivitySource HateoasAuthorizationSource = new("Explore.Hateoas.Authorization");

    private readonly IAuthorizationProvider _authorizationProvider;
    private readonly IEventRepository _eventRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<HateoasAuthorizationEvaluator> _logger;

    public HateoasAuthorizationEvaluator(
        IAuthorizationProvider authorizationProvider,
        IEventRepository eventRepository,
        ITenantContext tenantContext,
        ILogger<HateoasAuthorizationEvaluator> logger)
    {
        _authorizationProvider = authorizationProvider;
        _eventRepository = eventRepository;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates which links are allowed for the current user.
    /// Flow: static checks → build normalized checks → deduplicate → batch evaluate → map decisions back.
    /// Fail-closed: batch failure denies all permission-bound links.
    /// </summary>
    public async Task<IReadOnlyList<bool>> AreLinksAllowedAsync(
        IReadOnlyList<LinkDefinition> definitions,
        ClaimsPrincipal? user,
        HttpContext httpContext)
    {
        if (definitions.Count == 0)
            return [];

        var results = new bool[definitions.Count];
        var pendingChecks = new List<PendingCheck>();

        // Phase 1: Static checks (no provider call needed)
        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            if (!PassesStaticChecks(definition, user))
            {
                results[i] = false;
                continue;
            }

            if (RequiresExplicitPermissionAction(definition))
            {
                _logger.LogWarning(
                    "Link '{Rel}' for resource '{ResourceKind}' is permission-bound but has no explicit action. Denying link.",
                    definition.Rel,
                    definition.PermissionResourceKind);
                results[i] = false;
                continue;
            }

            var check = BuildCheck(definition);
            if (check is null)
            {
                results[i] = true;
                continue;
            }

            pendingChecks.Add(new PendingCheck(i, check));
        }

        if (pendingChecks.Count == 0)
            return results;

        try
        {
            pendingChecks = await ResolveTrustedEventFactsAsync(
                pendingChecks,
                results,
                httpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HATEOAS trusted resource context resolution failed; denying permission-bound links.");
            return results;
        }

        if (pendingChecks.Count == 0)
            return results;

        // Phase 2: Deduplicate — collapse identical checks before provider invocation
        var uniqueChecks = new List<AuthorizationRequest>();
        var keyToDecisionIndex = new Dictionary<AuthorizationRequestKey, int>();

        foreach (var pending in pendingChecks)
        {
            if (keyToDecisionIndex.ContainsKey(pending.Key))
                continue;

            keyToDecisionIndex[pending.Key] = uniqueChecks.Count;
            uniqueChecks.Add(pending.Check);
        }

        var deduplicatedCount = pendingChecks.Count - uniqueChecks.Count;
        if (deduplicatedCount > 0)
        {
            _logger.LogDebug(
                "HATEOAS authorization dedup: {InputCount} checks reduced to {UniqueCount} unique ({DeduplicatedCount} duplicates removed).",
                pendingChecks.Count,
                uniqueChecks.Count,
                deduplicatedCount);
        }

        // Phase 3: Batch evaluate unique checks with telemetry
        using var activity = HateoasAuthorizationSource.StartActivity("hateoas.capability_planning");
        activity?.SetTag("checks.total", pendingChecks.Count);
        activity?.SetTag("checks.unique", uniqueChecks.Count);
        activity?.SetTag("checks.deduplicated", deduplicatedCount);

        try
        {
            var allowed = await _authorizationProvider.AuthorizeBatchAsync(uniqueChecks);

            // Phase 4: Map decisions back to all original link indices via dedup key
            foreach (var pending in pendingChecks)
            {
                var decisionIndex = keyToDecisionIndex[pending.Key];
                results[pending.Index] = decisionIndex < allowed.Count && allowed[decisionIndex].IsAllowed;
            }

            activity?.SetTag("outcome", "success");
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HATEOAS batch authorization failed; denying all {Count} permission-bound links (fail-closed).", pendingChecks.Count);
            activity?.SetTag("outcome", "fail_closed");

            foreach (var pending in pendingChecks)
            {
                results[pending.Index] = false;
            }

            return results;
        }
    }

    private static bool PassesStaticChecks(LinkDefinition definition, ClaimsPrincipal? user)
    {
        if (definition.Condition is not null && !definition.Condition())
            return false;

        if (definition.RequiresAuth &&
            user?.Identity?.IsAuthenticated != true &&
            !definition.AdvertiseWhenAnonymous)
        {
            return false;
        }

        if (definition.RequiredRoles is { Count: > 0 })
        {
            if (user?.Identity?.IsAuthenticated != true)
                return false;

            var hasRequiredRole = definition.RequiredRoles.Any(user.IsInRole);
            if (!hasRequiredRole)
                return false;
        }

        return true;
    }

    private AuthorizationRequest? BuildCheck(LinkDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.PermissionResourceKind))
            return null;

        var action = definition.PermissionAction;
        Debug.Assert(!string.IsNullOrWhiteSpace(action), "Permission-bound links must be screened before BuildCheck.");

        var resourceId = definition.PermissionResourceId
            ?? ExtractResourceId(definition.RouteValues)
            ?? definition.RouteName;

        return new AuthorizationRequest(
            definition.PermissionResourceKind,
            resourceId,
            action!,
            definition.PermissionScope,
            definition.PermissionFacts);
    }

    private static bool RequiresExplicitPermissionAction(LinkDefinition definition) =>
        !string.IsNullOrWhiteSpace(definition.PermissionResourceKind) &&
        string.IsNullOrWhiteSpace(definition.PermissionAction);

    /// <summary>
    /// Rebuilds event authority from the database for the two candidate shapes a link policy cannot
    /// supply itself: registration forms, which only know their parent event id, and event-team links,
    /// which only know the event they hang off. Both then carry exactly the facts the MediatR resolver
    /// would produce, so an affordance and its endpoint cannot disagree.
    /// <para>
    /// A candidate whose event is missing or belongs to another tenant is dropped, which suppresses the
    /// link rather than asking the provider a question with untrusted inputs.
    /// </para>
    /// </summary>
    private async Task<List<PendingCheck>> ResolveTrustedEventFactsAsync(
        List<PendingCheck> pendingChecks,
        bool[] results,
        CancellationToken cancellationToken)
    {
        var requested = pendingChecks
            .Select(pending => RequiresTrustedEventLookup(pending.Check))
            .ToArray();
        Guid[] eventIds = requested
            .Where(eventId => eventId is { } id && id != Guid.Empty)
            .Select(eventId => eventId!.Value)
            .Distinct()
            .ToArray();
        if (requested.All(eventId => eventId is null))
            return pendingChecks;

        if (eventIds.Length > IEventRepository.MaximumAuthorizationTargetBatchSize)
            throw new InvalidOperationException("HAL event authorization batch exceeds the authorization lookup bound.");

        IReadOnlyDictionary<Guid, Event> events = eventIds.Length == 0
            ? new Dictionary<Guid, Event>()
            : (await _eventRepository.GetAuthorizationTargetsByIdsAsync(eventIds, cancellationToken))
                .Where(item => item.TenantId == _tenantContext.TenantId)
                .ToDictionary(item => item.Id);

        var resolved = new List<PendingCheck>(pendingChecks.Count);
        for (var i = 0; i < pendingChecks.Count; i++)
        {
            var pending = pendingChecks[i];
            if (requested[i] is not { } requestedEventId)
            {
                resolved.Add(pending);
                continue;
            }

            if (!events.TryGetValue(requestedEventId, out Event? eventEntity))
            {
                results[pending.Index] = false;
                continue;
            }

            resolved.Add(new PendingCheck(
                pending.Index,
                pending.Check with { Facts = TrustedEventFacts(eventEntity) }));
        }

        return resolved;
    }

    /// <summary>
    /// Returns the event id whose authority must be loaded server-side, or <see langword="null"/> when the
    /// candidate already carries descriptor-published facts and needs no lookup.
    /// <para>
    /// A registration form is always decided as its parent event, so a candidate that names no event is
    /// unresolvable rather than self-sufficient. It yields <see cref="Guid.Empty"/>, which no event can
    /// match, and the caller denies it.
    /// </para>
    /// </summary>
    private static Guid? RequiresTrustedEventLookup(AuthorizationRequest check)
    {
        if (check.ResourceKind == ResourceKinds.RegistrationForm)
        {
            return check.Facts switch
            {
                EventScopedAuthorizationFacts facts => facts.EventId,
                EventAuthorizationFacts facts => facts.EventId,
                _ => Guid.Empty
            };
        }

        if (check.ResourceKind == ResourceKinds.Event &&
            check.Action == AuthorizationActions.Events.ManageTeam &&
            check.Facts is null)
        {
            return Guid.TryParse(check.ResourceId, out Guid eventId) ? eventId : Guid.Empty;
        }

        return null;
    }

    private static EventAuthorizationFacts TrustedEventFacts(Event eventEntity) => new(
        eventEntity.TenantId,
        eventEntity.Id,
        eventEntity.ActorId,
        eventEntity.Actor?.UserId,
        eventEntity.Actor?.OrganizationId,
        eventEntity.Actor?.GroupId,
        eventEntity.OrganizerActorId,
        eventEntity.OrganizerActor?.UserId,
        eventEntity.OrganizerActor?.OrganizationId,
        eventEntity.OrganizerActor?.GroupId,
        eventEntity.EventProvenanceType?.MasterCode ?? eventEntity.EventProvenanceTypeId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        eventEntity.SubmittedByUserId);

    private sealed record PendingCheck(int Index, AuthorizationRequest Check)
    {
        public AuthorizationRequestKey Key { get; } = Check.ToDeduplicationKey();
    }

    private static string? ExtractResourceId(object? routeValues)
    {
        if (routeValues is null)
            return null;

        if (routeValues is IReadOnlyDictionary<string, object> readOnlyDictionary)
            return TryGetId(readOnlyDictionary.ToDictionary(x => x.Key, x => (object?)x.Value));

        if (routeValues is IDictionary<string, object> dictionary)
            return TryGetId(dictionary.ToDictionary(x => x.Key, x => (object?)x.Value));

        var values = routeValues
            .GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .ToDictionary(x => x.Name, x => x.GetValue(routeValues));

        return TryGetId(values);
    }

    private static string? TryGetId(IReadOnlyDictionary<string, object?> values)
    {
        return TryGet(values, "id")
            ?? TryGet(values, "tenantId")
            ?? TryGet(values, "organizationId")
            ?? TryGet(values, "did")
            ?? TryGet(values, "userId");
    }

    private static string? TryGet(IReadOnlyDictionary<string, object?> values, string key)
    {
        foreach (var pair in values)
        {
            if (!pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            return pair.Value?.ToString();
        }

        return null;
    }
}
