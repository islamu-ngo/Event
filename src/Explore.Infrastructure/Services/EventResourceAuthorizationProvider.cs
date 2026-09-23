using Cerbos.Api.V1.Effect;
using Cerbos.Sdk.Builder;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Services;

namespace Explore.Infrastructure.Services;

public sealed partial class EventResourceAuthorizationProvider(ICerbosClientFactory clients)
    : IEventResourceAuthorizationProvider
{
    private static readonly HashSet<string> SupportedActions =
    [
        AuthorizationActions.EventResources.View,
        AuthorizationActions.EventResources.ViewManagement,
        AuthorizationActions.EventResources.Create,
        AuthorizationActions.EventResources.Update,
        AuthorizationActions.EventResources.Publish,
        AuthorizationActions.EventResources.Unpublish,
        AuthorizationActions.EventResources.Archive,
        AuthorizationActions.EventResources.Delete,
        AuthorizationActions.EventResources.Access,
        AuthorizationActions.EventResources.Download,
        AuthorizationActions.EventResources.ViewAudit,
        AuthorizationActions.EventResources.Export,
        AuthorizationActions.EventResources.Moderate
    ];

    public async Task<EventResourceProviderDecision> CheckAsync(
        EventResourceProviderInput input, CancellationToken cancellationToken)
    {
        var decisions = await CheckBatchAsync([input], cancellationToken);
        return decisions[0];
    }

    public async Task<IReadOnlyList<EventResourceProviderDecision>> CheckBatchAsync(
        IReadOnlyList<EventResourceProviderInput> inputs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        cancellationToken.ThrowIfCancellationRequested();

        if (inputs.Count == 0)
            return [];

        var decisions = Enumerable.Repeat(EventResourceProviderDecision.Unavailable, inputs.Count).ToArray();
        if (inputs.Count > EventResourceAuthorityRequest.MaximumBatchChecks ||
            inputs.Select(input => input.Resource.Id).Distinct().Count() > EventResourceAuthorityRequest.MaximumBatchResources)
            return decisions;

        foreach (var indexedGroup in inputs
                     .Select((input, index) => new IndexedInput(index, input))
                     .GroupBy(item => new ProviderGroupKey(item.Input.Route, item.Input.Principal)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = indexedGroup.ToArray();
            var conflicts = group
                .GroupBy(item => (item.Input.Resource.Id, item.Input.Action))
                .Where(duplicates => duplicates.Select(item => (item.Input.Resource, item.Input.ParentModeration))
                    .Distinct().Skip(1).Any())
                .SelectMany(duplicates => duplicates)
                .Select(item => item.Index)
                .ToHashSet();

            var remote = new List<IndexedInput>(group.Length);
            foreach (var item in group)
            {
                if (conflicts.Contains(item.Index))
                    continue;

                var input = item.Input;
                if (!IsUsableRoute(input.Route))
                    continue;

                if (!IsValidInput(input))
                {
                    decisions[item.Index] = EventResourceProviderDecision.Deny;
                    continue;
                }

                if (input.Route.Mode == EventResourceProviderMode.Local)
                {
                    decisions[item.Index] = EventResourceProviderDecision.Allow;
                    continue;
                }

                remote.Add(item);
            }

            if (remote.Count > 0)
                await CheckRemoteGroupAsync(indexedGroup.Key, remote, decisions, cancellationToken);
        }

        await ApplyParentModerationAsync(inputs, decisions, cancellationToken);
        return decisions;
    }

    private async Task CheckRemoteGroupAsync(
        ProviderGroupKey group,
        IReadOnlyList<IndexedInput> inputs,
        EventResourceProviderDecision[] decisions,
        CancellationToken cancellationToken)
    {
        var workItems = inputs
            .GroupBy(item => item.Input.Resource)
            .Select(resourceGroup => new RemoteResource(
                resourceGroup.Key,
                resourceGroup.GroupBy(item => item.Input.Action)
                    .ToDictionary(actionGroup => actionGroup.Key, actionGroup => actionGroup.Select(item => item.Index).ToArray(),
                        StringComparer.Ordinal)))
            .ToArray();

        // Cerbos resource attributes apply to the whole resource entry, not to an individual action.
        // Put differing frozen projections for the same resource id in separate bounded requests.
        var requestGroups = new List<List<RemoteResource>>();
        foreach (var item in workItems)
        {
            var requestGroup = requestGroups.FirstOrDefault(candidate =>
                candidate.All(existing => existing.Resource.Id != item.Resource.Id));
            if (requestGroup is null)
            {
                requestGroup = [];
                requestGroups.Add(requestGroup);
            }
            requestGroup.Add(item);
        }

        foreach (var requestGroup in requestGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CheckRemoteRequestAsync(group, requestGroup, decisions, cancellationToken);
        }
    }

    private async Task CheckRemoteRequestAsync(
        ProviderGroupKey group,
        IReadOnlyList<RemoteResource> resources,
        EventResourceProviderDecision[] decisions,
        CancellationToken cancellationToken)
    {
        var principal = BuildPrincipal(group.Principal);
        var entries = resources.Select(item => BuildResourceEntry(group.Route, item)).ToArray();
        var request = CheckResourcesRequest.NewInstance().WithPrincipal(principal).WithResourceEntries(entries);

        try
        {
            var response = await clients.GetOrCreate(group.Route.GrpcEndpoint!)
                .CheckResourcesAsync(request, null).WaitAsync(cancellationToken);
            if (response is null || response.Raw.Results.Count != resources.Count)
                return;

            var expected = resources.ToDictionary(item => item.Resource.Id.ToString("D"), StringComparer.Ordinal);
            var bound = new Dictionary<RemoteResource, IReadOnlyDictionary<string, Effect>>();
            foreach (var result in response.Raw.Results)
            {
                if (result.Resource is null || result.Resource.Kind != ResourceKinds.EventResource ||
                    !expected.TryGetValue(result.Resource.Id, out var item) || bound.ContainsKey(item) ||
                    result.Actions.Count != item.Actions.Count ||
                    result.Actions.Keys.Any(action => !item.Actions.ContainsKey(action)) ||
                    result.Actions.Values.Any(effect => effect is not (Effect.Allow or Effect.Deny)))
                    return;

                bound.Add(item, result.Actions);
            }

            if (bound.Count != resources.Count)
                return;

            foreach (var item in resources)
            foreach (var (action, indexes) in item.Actions)
            {
                var decision = bound[item][action] == Effect.Allow
                    ? EventResourceProviderDecision.Allow
                    : EventResourceProviderDecision.Deny;
                foreach (var index in indexes)
                    decisions[index] = decision;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // This request's positions retain their bounded Unavailable defaults.
        }
    }

    private static bool IsUsableRoute(EventResourceProviderSnapshot route) =>
        route.IsUsable && route.Scope is not null && route.Scope == route.Scope.Trim() &&
        route.PolicyVersion == route.PolicyVersion.Trim();

    private static bool IsValidInput(EventResourceProviderInput input) =>
        input.Resource.DomainAllowed &&
        input.Principal.UserId != Guid.Empty &&
        input.Principal.TenantId != Guid.Empty &&
        input.Resource.Id != Guid.Empty &&
        input.Resource.TenantId != Guid.Empty &&
        input.Principal.TenantId == input.Resource.TenantId &&
        SupportedActions.Contains(input.Action) &&
        (input.Action != AuthorizationActions.EventResources.Moderate ||
            input.Principal.CanModerate && (input.Route.Mode == EventResourceProviderMode.Local ||
                IsCompleteParentModeration(input)));

    private static Principal BuildPrincipal(EventResourceProviderPrincipal subject) =>
        Principal.NewInstance(subject.UserId.ToString("D"), "islamuevent_authenticated_user")
            .WithAttribute("tenantId", AttributeValue.StringValue(subject.TenantId.ToString("D")))
            .WithAttribute("isTenantMember", AttributeValue.BoolValue(subject.IsTenantMember))
            .WithAttribute("controlsOrganizer", AttributeValue.BoolValue(subject.ControlsOrganizer))
            .WithAttribute("hasEventUpdate", AttributeValue.BoolValue(subject.HasEventUpdate))
            .WithAttribute("hasEventPublish", AttributeValue.BoolValue(subject.HasEventPublish))
            .WithAttribute("canModerate", AttributeValue.BoolValue(subject.CanModerate));

    private static ResourceEntry BuildResourceEntry(EventResourceProviderSnapshot route, RemoteResource item)
    {
        var resource = item.Resource;
        return ResourceEntry.NewInstance(ResourceKinds.EventResource, resource.Id.ToString("D"))
            .WithActions(item.Actions.Keys.ToArray())
            .WithScope(route.Scope)
            .WithPolicyVersion(route.PolicyVersion)
            .WithAttribute("tenantId", AttributeValue.StringValue(resource.TenantId.ToString("D")))
            .WithAttribute("eventId", AttributeValue.StringValue(resource.EventId.ToString("D")))
            .WithAttribute("sessionId", resource.SessionId is { } session
                ? AttributeValue.StringValue(session.ToString("D")) : AttributeValue.NullValue())
            .WithAttribute("publicationStateId", AttributeValue.DoubleValue(resource.PublicationStateId))
            .WithAttribute("disclosureModeId", AttributeValue.DoubleValue(resource.DisclosureModeId))
            .WithAttribute("kindId", AttributeValue.DoubleValue(resource.KindId))
            .WithAttribute("deliveryTypeId", AttributeValue.DoubleValue(resource.DeliveryTypeId))
            .WithAttribute("discloseMetadata", AttributeValue.BoolValue(resource.DiscloseMetadata))
            .WithAttribute("disclosePrivateMetadata", AttributeValue.BoolValue(resource.DisclosePrivateMetadata))
            .WithAttribute("canAccess", AttributeValue.BoolValue(resource.CanAccess))
            .WithAttribute("managementCeiling", AttributeValue.BoolValue(resource.ManagementCeiling))
            .WithAttribute("publicationCeiling", AttributeValue.BoolValue(resource.PublicationCeiling))
            .WithAttribute("isCreation", AttributeValue.BoolValue(resource.IsCreation))
            .WithAttribute("domainAllowed", AttributeValue.BoolValue(resource.DomainAllowed));
    }

    private sealed record IndexedInput(int Index, EventResourceProviderInput Input);
    private sealed record ProviderGroupKey(EventResourceProviderSnapshot Route, EventResourceProviderPrincipal Principal);
    private sealed record RemoteResource(
        EventResourceProviderResource Resource,
        IReadOnlyDictionary<string, int[]> Actions);
}
