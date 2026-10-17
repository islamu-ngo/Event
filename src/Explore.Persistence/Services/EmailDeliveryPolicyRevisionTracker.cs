
using System.Runtime.CompilerServices;
using Explore.Application.Models;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

internal static class EmailDeliveryPolicyRevisionTracker
{
    private static readonly ConditionalWeakTable<ExploreDbContext, TransactionBaselines> Baselines = new();

    public static async Task RecordInstanceAsync(ExploreDbContext context,
        EmailDeliveryPolicySet before, Guid? actorId, CancellationToken cancellationToken,
        MixedSettingsBatchStore? mixed = null, TypedSettingsOriginalStore? governance = null)
    {
        if (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql && mixed is null && governance is null)
            await PostgresEmailDeliveryPolicyContract.RequireMutationAsync(context, null, cancellationToken);
        TransactionBaselines baselines = GetBaselines(context);
        var after = governance is not null ? governance.GovernanceEmailPolicies()
            : mixed is null ? await EmailDeliveryPolicyReader.ReadAllAsync(context, cancellationToken) : mixed.EmailPolicies();
        await using var claim = await RelationalNamedLock.AcquireTransactionAsync(context,
            EmailDispatchOutboxRepository.ClaimAdvisoryLockName, cancellationToken);
        DateTime now = governance?.GovernanceOccurredAt ?? mixed?.OccurredAt ?? await RelationalDatabaseClock.GetUtcNowAsync(context, cancellationToken);
        await UpdateProcessorAsync(context, baselines, before.Instance, after.Instance,
            now, actorId, cancellationToken, mixed, governance);

        var affected = new Dictionary<Guid, (EmailDeliveryPolicySnapshot Before, EmailDeliveryPolicySnapshot After)>();
        foreach (var (tenantId, current) in after.Tenants)
        {
            // Tenant creation can occur between snapshots. A new scope had no earlier tenant override.
            var previous = before.Tenants.GetValueOrDefault(tenantId) ?? before.Instance;
            if (previous.TransportTenantId is null || current.TransportTenantId is null
                || previous.Enabled != current.Enabled || previous.State != current.State)
                affected.Add(tenantId, (previous, current));
        }

        await UpdateTenantsAsync(context, baselines, affected, now, actorId, cancellationToken, mixed, governance: governance);
    }

    public static async Task RecordTenantAsync(ExploreDbContext context, Guid tenantId,
        EmailDeliveryPolicySnapshot before, Guid? actorId, CancellationToken cancellationToken,
        MixedSettingsBatchStore? mixed = null, TypedSettingsOriginalStore? plan = null)
    {
        if (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql && mixed is null && plan is null)
            await PostgresEmailDeliveryPolicyContract.RequireMutationAsync(context, tenantId, cancellationToken);
        TransactionBaselines baselines = GetBaselines(context);
        var after = plan is not null ? plan.PlanEmailPolicies().Tenants[tenantId] : mixed is null
            ? await EmailDeliveryPolicyReader.ReadAsync(context, tenantId, cancellationToken)
            : mixed.EmailPolicies().Tenants[tenantId];
        await using var claim = await RelationalNamedLock.AcquireTransactionAsync(context,
            EmailDispatchOutboxRepository.ClaimAdvisoryLockName, cancellationToken);
        DateTime now = plan?.PlanOccurredAt ?? mixed?.OccurredAt ?? await RelationalDatabaseClock.GetUtcNowAsync(context, cancellationToken);
        await UpdateTenantsAsync(context, baselines,
            new Dictionary<Guid, (EmailDeliveryPolicySnapshot Before, EmailDeliveryPolicySnapshot After)>
            {
                [tenantId] = (before, after)
            },
            now, actorId, cancellationToken, mixed, plan);
    }

    private static async Task UpdateProcessorAsync(ExploreDbContext context, TransactionBaselines baselines,
        EmailDeliveryPolicySnapshot before, EmailDeliveryPolicySnapshot after,
        DateTime now, Guid? actorId, CancellationToken cancellationToken, MixedSettingsBatchStore? mixed,
        TypedSettingsOriginalStore? governance = null)
    {
        var existing = governance is not null ? governance.GovernanceRows<EmailDispatchProcessorState>()
                .SingleOrDefault(state => state.ProcessorCode == EmailDispatchOutboxRepository.SmtpProcessorCode)
            : mixed is not null ? mixed.Rows<EmailDispatchProcessorState>()
                .SingleOrDefault(state => state.ProcessorCode == EmailDispatchOutboxRepository.SmtpProcessorCode)
            : await context.EmailDispatchProcessorStates.AsNoTracking()
            .SingleOrDefaultAsync(state => state.ProcessorCode == EmailDispatchOutboxRepository.SmtpProcessorCode,
                cancellationToken);
        PolicyBaseline baseline = baselines.Processor ??= new PolicyBaseline(
            Policy: before, Revision: existing?.DeliveryPolicyRevision ?? 0,
            SuppressedThroughRevision: existing?.OptionalSuppressedThroughRevision,
            SuppressedThroughUtc: existing?.OptionalSuppressedThroughUtc);
        SuppressionBoundary suppression = ResolveSuppressionBoundary(baseline.Policy, after);
        long revision = checked(baseline.Revision + 1);
        DateTime? cutoff = ResolveCutoff(baseline, suppression, now);
        long? cutoffRevision = suppression switch
        {
            SuppressionBoundary.None => baseline.SuppressedThroughRevision,
            SuppressionBoundary.PreviousRevision => baseline.Revision,
            SuppressionBoundary.CurrentRevision => revision,
            _ => throw new InvalidOperationException("Unexpected email suppression boundary.")
        };

        if (mixed is not null || governance is not null)
        {
            var row = existing ?? new EmailDispatchProcessorState
            {
                Id = Guid.CreateVersion7(),
                ProcessorCode = EmailDispatchOutboxRepository.SmtpProcessorCode
            };
            row.DeliveryPolicyRevision = revision;
            row.OptionalSuppressedThroughRevision = cutoffRevision;
            row.OptionalSuppressedThroughUtc = cutoff;
            row.UpdatedAt = now;
            row.UpdatedBy = actorId;
            if (governance is not null)
                await governance.WriteGovernanceEmailRowsAsync([(row, existing is null ? EntityState.Added : EntityState.Modified)], cancellationToken);
            else
                await mixed!.WriteEmailRowsAsync([(row, existing is null ? EntityState.Added : EntityState.Modified)], cancellationToken);
            return;
        }

        if (existing is not null)
        {
            await context.EmailDispatchProcessorStates.Where(state => state.Id == existing.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(state => state.DeliveryPolicyRevision, revision)
                    .SetProperty(state => state.OptionalSuppressedThroughRevision, cutoffRevision)
                    .SetProperty(state => state.OptionalSuppressedThroughUtc, cutoff)
                    .SetProperty(state => state.UpdatedAt, now)
                    .SetProperty(state => state.UpdatedBy, actorId), cancellationToken);
            return;
        }

        var created = new EmailDispatchProcessorState
        {
            Id = Guid.CreateVersion7(),
            ProcessorCode = EmailDispatchOutboxRepository.SmtpProcessorCode,
            DeliveryPolicyRevision = revision,
            OptionalSuppressedThroughUtc = cutoff,
            OptionalSuppressedThroughRevision = cutoffRevision,
            UpdatedAt = now,
            UpdatedBy = actorId
        };
        context.EmailDispatchProcessorStates.Add(created);
        await context.SaveChangesAsync(cancellationToken);
        context.Entry(created).State = EntityState.Detached;
    }

    private static async Task UpdateTenantsAsync(ExploreDbContext context, TransactionBaselines baselines,
        IReadOnlyDictionary<Guid, (EmailDeliveryPolicySnapshot Before, EmailDeliveryPolicySnapshot After)> policies,
        DateTime now, Guid? actorId,
        CancellationToken cancellationToken, MixedSettingsBatchStore? mixed, TypedSettingsOriginalStore? plan = null,
        TypedSettingsOriginalStore? governance = null)
    {
        // Keep explicit tenant predicates below every supported provider's parameter ceiling.
        foreach (Guid[] tenantIds in policies.Keys.Chunk(500))
        {
            var controls = context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                .Where(control => tenantIds.Contains(control.TenantId));
            var existing = governance is not null
                ? governance.GovernanceRows<EmailDispatchTenantControl>().Where(control => tenantIds.Contains(control.TenantId))
                    .ToDictionary(control => control.TenantId, control => new PostgresEmailDeliveryPolicyContract.ControlSnapshot(
                        control.TenantId, control.DeliveryPolicyRevision,
                        control.OptionalSuppressedThroughRevision, control.OptionalSuppressedThroughUtc))
                : plan is not null
                ? plan.PlanRows<EmailDispatchTenantControl>().Where(control => tenantIds.Contains(control.TenantId))
                    .ToDictionary(control => control.TenantId, control => new PostgresEmailDeliveryPolicyContract.ControlSnapshot(
                        control.TenantId, control.DeliveryPolicyRevision,
                        control.OptionalSuppressedThroughRevision, control.OptionalSuppressedThroughUtc))
                : mixed is not null
                ? mixed.Rows<EmailDispatchTenantControl>().Where(control => tenantIds.Contains(control.TenantId))
                    .ToDictionary(control => control.TenantId, control => new PostgresEmailDeliveryPolicyContract.ControlSnapshot(
                        control.TenantId, control.DeliveryPolicyRevision,
                        control.OptionalSuppressedThroughRevision, control.OptionalSuppressedThroughUtc))
                : RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql
                ? await PostgresEmailDeliveryPolicyContract.ReadControlsAsync(context, tenantIds, cancellationToken)
                : await controls.AsNoTracking()
                    .Select(control => new PostgresEmailDeliveryPolicyContract.ControlSnapshot(
                        control.TenantId, control.DeliveryPolicyRevision,
                        control.OptionalSuppressedThroughRevision, control.OptionalSuppressedThroughUtc))
                    .ToDictionaryAsync(control => control.TenantId, cancellationToken);
            Guid[] firstTouches = tenantIds.Where(tenantId => !baselines.Tenants.ContainsKey(tenantId)).ToArray();
            foreach (Guid tenantId in firstTouches)
            {
                var control = existing.GetValueOrDefault(tenantId);
                baselines.Tenants.Add(tenantId, new PolicyBaseline(
                    Policy: policies[tenantId].Before, Revision: control?.DeliveryPolicyRevision ?? 0,
                    SuppressedThroughRevision: control?.OptionalSuppressedThroughRevision,
                    SuppressedThroughUtc: control?.OptionalSuppressedThroughUtc));
            }

            if (tenantIds.Any(tenantId => baselines.Tenants[tenantId].Revision == long.MaxValue))
                throw new OverflowException("Email delivery policy revision is exhausted.");

            if (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql || mixed is not null || plan is not null || governance is not null)
            {
                var changes = tenantIds.Select(tenantId =>
                {
                    var baseline = baselines.Tenants[tenantId];
                    var current = existing.GetValueOrDefault(tenantId);
                    long revision = checked(baseline.Revision + 1);
                    var boundary = ResolveSuppressionBoundary(baseline.Policy, policies[tenantId].After);
                    long? suppressed = boundary switch
                    {
                        SuppressionBoundary.PreviousRevision => baseline.Revision,
                        SuppressionBoundary.CurrentRevision => revision,
                        _ => baseline.SuppressedThroughRevision
                    };
                    DateTime? cutoff = boundary == SuppressionBoundary.None
                        ? baseline.SuppressedThroughUtc
                        : current?.OptionalSuppressedThroughUtc is { } previous && previous > now ? previous : now;
                    return new PostgresEmailDeliveryPolicyContract.ControlChange(
                        tenantId, baseline.Revision, current?.DeliveryPolicyRevision ?? 0, revision, suppressed, cutoff);
                }).ToArray();
                if (governance is not null)
                    await governance.WriteGovernanceEmailControlsAsync(changes, now, actorId, cancellationToken);
                else if (plan is not null)
                    await plan.WritePlanEmailControlsAsync(changes, now, actorId, cancellationToken);
                else if (mixed is not null)
                    await mixed.WriteEmailControlsAsync(changes, now, actorId, cancellationToken);
                else
                    await PostgresEmailDeliveryPolicyContract.ApplyControlsAsync(context, changes, now, actorId, cancellationToken);
                continue;
            }

            if (firstTouches.Length > 0)
                await controls.Where(control => firstTouches.Contains(control.TenantId))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(control => control.DeliveryPolicyRevision, control => control.DeliveryPolicyRevision + 1)
                        .SetProperty(control => control.UpdatedAt, now)
                        .SetProperty(control => control.UpdatedBy, actorId), cancellationToken);

            Guid[] repeatTouches = tenantIds.Except(firstTouches).ToArray();
            if (repeatTouches.Length > 0)
                await controls.Where(control => repeatTouches.Contains(control.TenantId))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(control => control.UpdatedAt, now)
                        .SetProperty(control => control.UpdatedBy, actorId), cancellationToken);

            var suppressionByTenant = tenantIds.ToDictionary(tenantId => tenantId,
                tenantId => ResolveSuppressionBoundary(baselines.Tenants[tenantId].Policy, policies[tenantId].After));

            foreach (var boundary in new[] { SuppressionBoundary.PreviousRevision, SuppressionBoundary.CurrentRevision })
            {
                Guid[] scopedIds = tenantIds.Where(tenantId => suppressionByTenant[tenantId] == boundary).ToArray();
                if (scopedIds.Length == 0)
                    continue;
                long revisionOffset = boundary == SuppressionBoundary.PreviousRevision ? 1 : 0;
                await controls.Where(control => scopedIds.Contains(control.TenantId))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(control => control.OptionalSuppressedThroughRevision,
                            control => control.DeliveryPolicyRevision - revisionOffset)
                        .SetProperty(control => control.OptionalSuppressedThroughUtc,
                            control => control.OptionalSuppressedThroughUtc == null || control.OptionalSuppressedThroughUtc < now
                                ? now : control.OptionalSuppressedThroughUtc), cancellationToken);
            }

            // A host-only leaf may temporarily invalidate a coherent atomic host/sender edit.
            // Restore the transaction's original history when its final policy is available again.
            var restorations = tenantIds.Where(tenantId => suppressionByTenant[tenantId] == SuppressionBoundary.None
                    && existing.TryGetValue(tenantId, out var control)
                    && (control.OptionalSuppressedThroughRevision != baselines.Tenants[tenantId].SuppressedThroughRevision
                        || control.OptionalSuppressedThroughUtc != baselines.Tenants[tenantId].SuppressedThroughUtc))
                .GroupBy(tenantId => (baselines.Tenants[tenantId].SuppressedThroughRevision,
                    baselines.Tenants[tenantId].SuppressedThroughUtc));
            // ponytail: rare restoration takes one update per distinct baseline in this 500-tenant chunk;
            // consider bulk restoration only if measured policy-edit workloads justify it.
            foreach (var restoration in restorations)
            {
                Guid[] scopedIds = restoration.ToArray();
                var original = restoration.Key;
                await controls.Where(control => scopedIds.Contains(control.TenantId))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(control => control.OptionalSuppressedThroughRevision, original.SuppressedThroughRevision)
                        .SetProperty(control => control.OptionalSuppressedThroughUtc, original.SuppressedThroughUtc), cancellationToken);
            }

            var created = tenantIds.Where(tenantId => !existing.ContainsKey(tenantId))
                .Select(tenantId => new EmailDispatchTenantControl
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    DeliveryPolicyRevision = checked(baselines.Tenants[tenantId].Revision + 1),
                    OptionalSuppressedThroughUtc = ResolveCutoff(baselines.Tenants[tenantId], suppressionByTenant[tenantId], now),
                    OptionalSuppressedThroughRevision = suppressionByTenant[tenantId] switch
                    {
                        SuppressionBoundary.PreviousRevision => baselines.Tenants[tenantId].Revision,
                        SuppressionBoundary.CurrentRevision => checked(baselines.Tenants[tenantId].Revision + 1),
                        _ => baselines.Tenants[tenantId].SuppressedThroughRevision
                    },
                    CreatedAt = now,
                    CreatedBy = actorId,
                    UpdatedAt = now,
                    UpdatedBy = actorId
                }).ToArray();
            if (created.Length == 0)
                continue;
            context.EmailDispatchTenantControls.AddRange(created);
            await context.SaveChangesAsync(cancellationToken);
            foreach (var control in created)
                context.Entry(control).State = EntityState.Detached;
        }
    }

    private static SuppressionBoundary ResolveSuppressionBoundary(
        EmailDeliveryPolicySnapshot before, EmailDeliveryPolicySnapshot after) =>
        after.State != EmailDeliveryState.Available ? SuppressionBoundary.CurrentRevision
        : before.State != EmailDeliveryState.Available ? SuppressionBoundary.PreviousRevision
        : SuppressionBoundary.None;

    private static DateTime? ResolveCutoff(PolicyBaseline baseline, SuppressionBoundary suppression, DateTime now) =>
        suppression != SuppressionBoundary.None
            && (!baseline.SuppressedThroughUtc.HasValue || baseline.SuppressedThroughUtc.Value < now)
                ? now : baseline.SuppressedThroughUtc;

    private static TransactionBaselines GetBaselines(ExploreDbContext context)
    {
        Guid transactionId = context.Database.CurrentTransaction?.TransactionId
            ?? throw new InvalidOperationException("Email policy reconciliation requires an active transaction.");
        if (Baselines.TryGetValue(context, out var baselines) && baselines.TransactionId == transactionId)
            return baselines;

        // EF execution-strategy retries open a new transaction even when they reuse the same context.
        Baselines.Remove(context);
        baselines = new TransactionBaselines(transactionId);
        Baselines.Add(context, baselines);
        return baselines;
    }

    private sealed record PolicyBaseline(EmailDeliveryPolicySnapshot Policy, long Revision,
        long? SuppressedThroughRevision, DateTime? SuppressedThroughUtc);

    private sealed class TransactionBaselines(Guid transactionId)
    {
        public Guid TransactionId { get; } = transactionId;
        public PolicyBaseline? Processor { get; set; }
        public Dictionary<Guid, PolicyBaseline> Tenants { get; } = [];
    }

    private enum SuppressionBoundary { None, PreviousRevision, CurrentRevision }
}
