using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Application.Utilities;
using Explore.Domain.Constants;

namespace Explore.Infrastructure.Services;

public enum HumanAuthorizationRouteProvider
{
    Denied,
    Local,
    CerbosInstance,
    CerbosTenant
}

/// <summary>Snapshot deployment scalars once before preparation; never retain mutable options.</summary>
public sealed record HumanAuthorizationRouteDeployment(
    string? Provider, string InstanceGrpcEndpoint, bool UsePolicyScope)
{
    public static HumanAuthorizationRouteDeployment Capture(
        AuthorizationProviderDeploymentOptions authorization, CerbosSettings cerbos)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(cerbos);
        return new(authorization.GetProvider(), cerbos.GrpcEndpoint, cerbos.UsePolicyScope);
    }
}

/// <summary>
/// Immutable evaluator selection, not a grant. Null PolicyVersion means omit SDK WithPolicyVersion
/// and retain its default. AdminEndpoint is observed configuration, not a decision route input.
/// </summary>
public sealed record HumanAuthorizationRoute(
    Guid PrincipalId,
    Guid? SelectedTenantId,
    HumanAuthorizationRouteProvider Provider,
    string? Endpoint,
    string? PolicyScope,
    string? PolicyVersion,
    string? AdminEndpoint,
    string? DenialReason)
{
    public bool IsSemanticallyEquivalentTo(HumanAuthorizationRoute other) =>
        PrincipalId == other.PrincipalId && SelectedTenantId == other.SelectedTenantId
        && Provider == other.Provider && Endpoint == other.Endpoint && PolicyScope == other.PolicyScope
        && PolicyVersion == other.PolicyVersion && DenialReason == other.DenialReason;

    public override string ToString() => nameof(HumanAuthorizationRoute);
}

/// <summary>
/// Pure projection of fresh persisted facts. Shares real hierarchical precedence and typed fallback,
/// not the cached resolver or instance-mode cache. Settings always select the platform provider.
/// </summary>
public static class HumanAuthorizationRouteProjector
{
    public static HumanAuthorizationRoute Project(
        HumanAuthorizationRouteFacts facts,
        HumanAuthorizationRouteDeployment deployment,
        bool settingsGovernance,
        bool localDecisionSourcesCoLocated = false)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(deployment);
        if (facts.PrincipalId == Guid.Empty || facts.SelectedTenantId == Guid.Empty
            || facts.Settings.IsDefault || facts.Settings.Length != HumanAuthorizationRouteFacts.Keys.Length
            || !facts.Settings.Select(row => row.Key).SequenceEqual(HumanAuthorizationRouteFacts.Keys)
            || (!facts.SelectedTenantId.HasValue && facts.Settings.Any(row => row.Tenant is not null)))
            throw new InvalidOperationException("human_route_facts_contract_invalid");

        var rows = facts.Settings.ToDictionary(row => row.Key, StringComparer.Ordinal);
        string? Value(string key) => HierarchicalSettingMerge.Resolve(key, new SettingMergeFacts(
            rows[key].System, rows[key].Tenant, null, null, null, facts.SelectedTenantId.HasValue, null))?.Value;
        string? StringValue(string key) => SettingValueSerializer.Deserialize<string?>(Value(key), null);
        string? scope = deployment.UsePolicyScope ? facts.SelectedTenantId?.ToString("D") : null;
        HumanAuthorizationRoute Route(HumanAuthorizationRouteProvider provider, string? endpoint = null,
            string? admin = null, string? denial = null) =>
            new(facts.PrincipalId, facts.SelectedTenantId, provider, endpoint,
                provider is HumanAuthorizationRouteProvider.CerbosInstance or HumanAuthorizationRouteProvider.CerbosTenant
                    ? scope : null, null, admin, denial);

        // ResolveTenantByoConfigAsync precedes instance mode even when deployment selects Local.
        // Settings bypass this entire path: platform governance must never be sent to tenant BYO.
        if (!settingsGovernance && facts.SelectedTenantId.HasValue
            && SettingValueSerializer.Deserialize(Value(GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled), false)
            && StringValue(GovernanceSettingKeys.Cerbos.Mode)?.Trim().ToLowerInvariant() == "custom_endpoint")
        {
            string? custom = StringValue(GovernanceSettingKeys.Cerbos.CustomEndpoint);
            string? rawAdmin = StringValue(GovernanceSettingKeys.Cerbos.CustomAdminEndpoint);
            string? admin = string.IsNullOrWhiteSpace(rawAdmin) ? null : GrpcEndpointNormalizer.Normalize(rawAdmin);
            return string.IsNullOrWhiteSpace(custom)
                ? Route(HumanAuthorizationRouteProvider.Denied, admin: admin, denial: "custom_endpoint_missing")
                : Route(HumanAuthorizationRouteProvider.CerbosTenant, GrpcEndpointNormalizer.Normalize(custom), admin);
        }

        // RuntimeAuthorizationProvider reads the raw INSTANCE provider row, not hierarchical defaults
        // or tenant overrides. Every non-Cerbos persisted value retains its existing Local semantics.
        string? deploymentProvider = new AuthorizationProviderDeploymentOptions { Provider = deployment.Provider }.GetProvider();
        string? instanceProvider = SettingValueSerializer.Deserialize<string?>(
            rows[GovernanceSettingKeys.Security.AuthorizationProvider].System?.Value, null)?.Trim().ToLowerInvariant();
        if ((deploymentProvider ?? instanceProvider) != AuthorizationProviderDeploymentOptions.CerbosProvider)
            return localDecisionSourcesCoLocated
                ? Route(HumanAuthorizationRouteProvider.Local)
                : Route(HumanAuthorizationRouteProvider.Denied, denial: "local_decision_sources_not_colocated");

        // Generic runtime instance routing uses the configured SDK client endpoint. A persisted
        // cerbos.grpc_endpoint row is observed/fenced but does not replace deployment CerbosSettings.
        string endpoint = GrpcEndpointNormalizer.Normalize(deployment.InstanceGrpcEndpoint);
        return string.IsNullOrWhiteSpace(endpoint)
            ? Route(HumanAuthorizationRouteProvider.Denied, denial: "instance_endpoint_missing")
            : Route(HumanAuthorizationRouteProvider.CerbosInstance, endpoint);
    }
}
