using System.Linq.Expressions;
using Explore.Domain.Policies;
using Explore.Domain.Settings;

namespace Explore.Application.Contracts.Services;

public sealed record PolicyDecision<T>(
    T? Value,
    bool CanOverride,
    SettingScope SourceScope,
    SettingScope? BlockedByScope);

public interface IPolicyResolver
{
    Task<PolicyDecision<T>> ResolveForTenantAsync<T>(
        Expression<Func<InstancePolicySet, PolicySlot<T>>> instanceSelector,
        Expression<Func<TenantPolicySet, PolicySlot<T>>> tenantSelector,
        Guid tenantId,
        CancellationToken ct = default);

    Task<PolicyDecision<T>> ResolveForOrganizationAsync<T>(
        Expression<Func<InstancePolicySet, PolicySlot<T>>> instanceSelector,
        Expression<Func<TenantPolicySet, PolicySlot<T>>> tenantSelector,
        Expression<Func<OrganizationPolicySet, PolicySlot<T>>> orgSelector,
        Guid organizationId,
        CancellationToken ct = default);
}
