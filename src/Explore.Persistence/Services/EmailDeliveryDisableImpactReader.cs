// ABOUTME: Reads the counterfactual email-disable impact within the caller's transaction.
// ABOUTME: Returns only scope revisions and lock state without mutating settings or delivery controls.

using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed class EmailDeliveryDisableImpactReader(ExploreDbContext context) : IEmailDeliveryDisableImpactReader
{
    public async Task<EmailDeliveryDisableImpactSnapshot?> ReadAsync(
        Guid? tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("An email delivery scope must identify a tenant or the instance.", nameof(tenantId));
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Email disable previews require an active transaction.");

        var policies = await EmailDeliveryPolicyReader.ReadDisableAsync(context, tenantId, cancellationToken);
        if (policies is null)
            return null;

        if (tenantId is { } id)
        {
            long revision = await context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .AsNoTracking().Where(control => control.TenantId == id)
                .Select(control => (long?)control.DeliveryPolicyRevision)
                .SingleOrDefaultAsync(cancellationToken) ?? 0;
            return new(TenantId: tenantId, Revision: revision, IsLocked: policies.IsLocked,
                AffectedScopes: policies.Before.Tenants[id].Enabled && !policies.After.Tenants[id].Enabled
                    ? [new(TenantId: id, Revision: revision)] : []);
        }

        long instanceRevision = await context.EmailDispatchProcessorStates.AsNoTracking()
            .Where(state => state.ProcessorCode == EmailDispatchOutboxRepository.SmtpProcessorCode)
            .Select(state => (long?)state.DeliveryPolicyRevision)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;
        var affected = ImmutableArray.CreateBuilder<EmailDeliveryAffectedScope>();
        if (policies.Before.Instance.Enabled && !policies.After.Instance.Enabled)
            affected.Add(new(TenantId: null, Revision: instanceRevision));

        // Keep revision lookups below every supported provider's parameter ceiling.
        foreach (var tenantIds in policies.Before.Tenants
                     .Where(pair => pair.Value.Enabled && !policies.After.Tenants[pair.Key].Enabled)
                     .Select(pair => pair.Key).Order().Chunk(500))
        {
            var revisions = await context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                .AsNoTracking().Where(control => tenantIds.Contains(control.TenantId))
                .ToDictionaryAsync(control => control.TenantId, control => control.DeliveryPolicyRevision, cancellationToken);
            foreach (Guid affectedTenant in tenantIds)
                affected.Add(new(TenantId: affectedTenant, Revision: revisions.GetValueOrDefault(affectedTenant)));
        }
        return new(TenantId: null, Revision: instanceRevision, IsLocked: false, AffectedScopes: affected.ToImmutable());
    }
}
