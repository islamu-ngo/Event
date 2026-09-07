using System.Linq.Expressions;
using Explore.Application.Contracts.Services;
using Explore.Domain.Policies;
using Explore.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public class PolicyResolver : IPolicyResolver
{
    private readonly ExploreDbContext _dbContext;

    public PolicyResolver(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PolicyDecision<T>> ResolveForTenantAsync<T>(
        Expression<Func<InstancePolicySet, PolicySlot<T>>> instanceSelector,
        Expression<Func<TenantPolicySet, PolicySlot<T>>> tenantSelector,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instancePolicy = await _dbContext.InstancePolicySets.FirstOrDefaultAsync(ct);
        var tenantPolicy = await _dbContext.TenantPolicySets
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);

        var instanceSlot = instancePolicy is not null
            ? instanceSelector.Compile()(instancePolicy)
            : null;

        var tenantSlot = tenantPolicy is not null
            ? tenantSelector.Compile()(tenantPolicy)
            : null;

        return Resolve(instanceSlot, tenantSlot, SettingScope.Tenant);
    }

    public async Task<PolicyDecision<T>> ResolveForOrganizationAsync<T>(
        Expression<Func<InstancePolicySet, PolicySlot<T>>> instanceSelector,
        Expression<Func<TenantPolicySet, PolicySlot<T>>> tenantSelector,
        Expression<Func<OrganizationPolicySet, PolicySlot<T>>> orgSelector,
        Guid organizationId,
        CancellationToken ct = default)
    {
        var orgPolicy = await _dbContext.OrganizationPolicySets
            .FirstOrDefaultAsync(o => o.OrganizationId == organizationId, ct);

        var tenantId = orgPolicy?.TenantId ?? Guid.Empty;

        var instancePolicy = await _dbContext.InstancePolicySets.FirstOrDefaultAsync(ct);
        var tenantPolicy = tenantId != Guid.Empty
            ? await _dbContext.TenantPolicySets
                .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct)
            : null;

        var instanceSlot = instancePolicy is not null
            ? instanceSelector.Compile()(instancePolicy)
            : null;

        var tenantSlot = tenantPolicy is not null
            ? tenantSelector.Compile()(tenantPolicy)
            : null;

        var orgSlot = orgPolicy is not null
            ? orgSelector.Compile()(orgPolicy)
            : null;

        return Resolve(instanceSlot, tenantSlot, orgSlot);
    }

    private static PolicyDecision<T> Resolve<T>(
        PolicySlot<T>? instanceSlot,
        PolicySlot<T>? tenantSlot,
        SettingScope requestingScope)
    {
        if (instanceSlot is null)
        {
            return Empty<T>();
        }

        if (instanceSlot.OverrideMode == ChildOverrideMode.Deny || requestingScope == SettingScope.Instance)
        {
            return new PolicyDecision<T>(
                ValueOrDefault(instanceSlot),
                requestingScope == SettingScope.Instance,
                SettingScope.Instance,
                instanceSlot.OverrideMode == ChildOverrideMode.Deny ? SettingScope.Instance : null);
        }

        if (tenantSlot is not null && HasValue(tenantSlot))
        {
            return new PolicyDecision<T>(
                tenantSlot.LocalValue,
                true,
                SettingScope.Tenant,
                null);
        }

        return new PolicyDecision<T>(
            ValueOrDefault(instanceSlot),
            true,
            SettingScope.Instance,
            null);
    }

    private static PolicyDecision<T> Resolve<T>(
        PolicySlot<T>? instanceSlot,
        PolicySlot<T>? tenantSlot,
        PolicySlot<T>? orgSlot)
    {
        if (instanceSlot is null)
        {
            return Empty<T>();
        }

        if (instanceSlot.OverrideMode == ChildOverrideMode.Deny)
        {
            return new PolicyDecision<T>(
                ValueOrDefault(instanceSlot),
                false,
                SettingScope.Instance,
                SettingScope.Instance);
        }

        if (tenantSlot is not null && tenantSlot.OverrideMode == ChildOverrideMode.Deny)
        {
            var effectiveValue = HasValue(tenantSlot)
                ? tenantSlot.LocalValue
                : ValueOrDefault(instanceSlot);

            return new PolicyDecision<T>(
                effectiveValue,
                false,
                HasValue(tenantSlot) ? SettingScope.Tenant : SettingScope.Instance,
                SettingScope.Tenant);
        }

        if (orgSlot is not null && HasValue(orgSlot))
        {
            return new PolicyDecision<T>(
                orgSlot.LocalValue,
                true,
                SettingScope.Organization,
                null);
        }

        if (tenantSlot is not null && HasValue(tenantSlot))
        {
            return new PolicyDecision<T>(
                tenantSlot.LocalValue,
                true,
                SettingScope.Tenant,
                null);
        }

        return new PolicyDecision<T>(
            ValueOrDefault(instanceSlot),
            true,
            SettingScope.Instance,
            null);
    }

    private static bool HasValue<T>(PolicySlot<T> slot) =>
        slot.LocalValue is not null;

    private static T? ValueOrDefault<T>(PolicySlot<T> slot) =>
        slot.LocalValue;

    private static PolicyDecision<T> Empty<T>() =>
        new(default, true, SettingScope.Instance, null);
}
