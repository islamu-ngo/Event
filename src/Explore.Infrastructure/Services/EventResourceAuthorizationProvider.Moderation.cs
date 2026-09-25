using Cerbos.Api.V1.Effect;
using Cerbos.Sdk.Builder;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Services;

namespace Explore.Infrastructure.Services;

public sealed partial class EventResourceAuthorizationProvider
{
    private static readonly string[] ParentModerationActions =
        [AuthorizationActions.Events.ModerateLight, AuthorizationActions.Events.ModerateHeavy];

    private static bool IsCompleteParentModeration(EventResourceProviderInput input) =>
        input.ParentModeration is { } parent && parent.Route.IsUsable
        && parent.Route == input.Route.ParentEventPolicy
        && parent.Principal.UserId == input.Principal.UserId
        && parent.Resource.TenantId == input.Resource.TenantId
        && parent.Resource.EventId == input.Resource.EventId && parent.Resource.EventId != Guid.Empty
        && parent.Principal.CanModerate(parent.Resource.TenantId)
        && parent.Principal.EventAssignments.Count <= EventResourceAuthorityRequest.MaximumBatchResources
        && parent.Principal.EventAssignments.All(value => value.TenantId == parent.Resource.TenantId)
        && parent.Principal.EventAssignments.Select(value => value.EventId).Distinct().Count()
            == parent.Principal.EventAssignments.Count
        && parent.Principal.EventAssignments.Any(value => value.EventId == parent.Resource.EventId);

    private async Task ApplyParentModerationAsync(IReadOnlyList<EventResourceProviderInput> inputs,
        EventResourceProviderDecision[] decisions, CancellationToken cancellationToken)
    {
        // Deduplicate across resource-principal groups: siblings may have different resource projections
        // but must share the same native parent principal, deployment fence and parent route.
        var groups = inputs.Select((input, index) => new IndexedInput(index, input))
            .Where(item => item.Input.Route.Mode == EventResourceProviderMode.Remote
                && item.Input.Action == AuthorizationActions.EventResources.Moderate
                && decisions[item.Index] == EventResourceProviderDecision.Allow)
            .GroupBy(item => new ParentGroupKey(item.Input.Route, item.Input.ParentModeration!.Principal));
        foreach (var group in groups)
        {
            var parents = group.GroupBy(item => item.Input.ParentModeration!.Resource)
                .ToDictionary(values => values.Key, values => values.Select(value => value.Index).ToArray());
            foreach (int index in parents.Values.SelectMany(value => value))
                decisions[index] = EventResourceProviderDecision.Unavailable;
            if (parents.Count > EventResourceAuthorityRequest.MaximumBatchResources
                || parents.Keys.Select(value => value.EventId).Distinct().Count() != parents.Count)
                continue;

            var route = group.Key.Route.ParentEventPolicy!;
            var entries = parents.Keys.OrderBy(value => value.EventId).Select(parent =>
            {
                var entry = ResourceEntry.NewInstance(ResourceKinds.Event, parent.EventId.ToString("D"))
                    .WithScope(route.Scope).WithPolicyVersion(route.PolicyVersion).WithActions(ParentModerationActions);
                foreach (var (key, value) in AuthorizationFactAttributeProjection.ToAttributes(parent)!)
                    entry = entry.WithAttribute(key, AttributeValue.StringValue((string)value));
                return entry;
            }).ToArray();
            var request = CheckResourcesRequest.NewInstance().WithPrincipal(BuildParentPrincipal(group.Key.Principal))
                .WithResourceEntries(entries);
            try
            {
                var response = await clients.GetOrCreate(group.Key.Route.GrpcEndpoint!)
                    .CheckResourcesAsync(request, null).WaitAsync(cancellationToken);
                if (response is null || response.Raw.Results.Count != parents.Count) continue;
                var expected = parents.ToDictionary(value => value.Key.EventId.ToString("D"), value => value.Value,
                    StringComparer.Ordinal);
                var bound = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (var result in response.Raw.Results)
                {
                    if (result.Resource is null || result.Resource.Kind != ResourceKinds.Event
                        || !expected.ContainsKey(result.Resource.Id) || bound.ContainsKey(result.Resource.Id)
                        || result.Actions.Count != ParentModerationActions.Length
                        || ParentModerationActions.Any(action => !result.Actions.ContainsKey(action))
                        || result.Actions.Values.Any(effect => effect is not (Effect.Allow or Effect.Deny)))
                        break;
                    bound.Add(result.Resource.Id, result.Actions.Values.Any(effect => effect == Effect.Allow));
                }
                if (bound.Count != parents.Count) continue;
                foreach (var (id, allowed) in bound)
                foreach (int index in expected[id])
                    decisions[index] = allowed ? EventResourceProviderDecision.Allow : EventResourceProviderDecision.Deny;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // No generic fallback: all positions in this parent batch retain Unavailable.
            }
        }
    }

    private static Principal BuildParentPrincipal(EventModerationPrincipal principal)
    {
        static AttributeValue Memberships(IEnumerable<Guid> values) => AttributeValue.MapValue(values.Order()
            .ToDictionary(value => value.ToString("D"), _ => AttributeValue.StringValue("admin"), StringComparer.Ordinal));
        static AttributeValue Ids(IEnumerable<Guid> values) => AttributeValue.ListValue(values.Order()
            .Select(value => AttributeValue.StringValue(value.ToString("D"))).ToArray());
        static AttributeValue Codes(IEnumerable<string> values) => AttributeValue.ListValue(values.Order(StringComparer.Ordinal)
            .Select(AttributeValue.StringValue).ToArray());
        var assignments = principal.EventAssignments.OrderBy(value => value.EventId)
            .ToDictionary(value => value.EventId.ToString("D"), value => AttributeValue.MapValue(new()
            {
                ["tenantId"] = AttributeValue.StringValue(value.TenantId.ToString("D")),
                ["roles"] = Codes(value.Roles), ["permissions"] = Codes(value.Permissions)
            }), StringComparer.Ordinal);
        return Principal.NewInstance(principal.UserId.ToString("D"), "islamuevent_authenticated_user")
            .WithAttribute("userId", AttributeValue.StringValue(principal.UserId.ToString("D")))
            .WithAttribute("isInstanceAdmin", AttributeValue.BoolValue(principal.IsInstanceAdmin))
            .WithAttribute("tenantMemberships", Memberships(principal.AdminTenantIds))
            .WithAttribute("orgMemberships", Memberships(principal.AdminOrganizationIds))
            .WithAttribute("groupMemberships", Memberships(principal.AdminGroupIds))
            .WithAttribute("eventCreateOrganizations", Ids(principal.EventCreateOrganizationIds))
            .WithAttribute("eventCreateGroups", Ids(principal.EventCreateGroupIds))
            .WithAttribute("eventFinanceOrganizations", Ids(principal.EventFinanceOrganizationIds))
            .WithAttribute("eventFinanceGroups", Ids(principal.EventFinanceGroupIds))
            .WithAttribute("eventAssignments", AttributeValue.MapValue(assignments));
    }

    private sealed record ParentGroupKey(EventResourceProviderSnapshot Route, EventModerationPrincipal Principal);
}
