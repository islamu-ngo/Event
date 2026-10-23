using System.Data.Common;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Models.InternalEvents;
using Explore.Application.Services;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Messaging;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Queries;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>Native SQLite lease guards, authored but not executed until programme assurance.</summary>
public sealed class ModerationEffectLeasePersistenceTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EffectPurposeDoesNotReadOriginalPayloadAtItsRetentionDeadline(bool expired)
    {
        await using var database = await Database.CreateAsync();
        var deadline = Now.AddMinutes(1);
        var id = await database.SeedAsync(capture: "inert", captureRetainUntil: deadline);
        ModerationEffectIntent claimed;
        await using (var claimant = database.Context())
        {
            var machine = await database.AuthenticateAsync(claimant);
            var claim = await database.Repository(claimant, machine).ClaimAsync(id, 1);
            await Assert.That(claim.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Known);
            claimed = claim.Effect!;
        }
        if (expired) database.Clock.UtcNow = deadline;
        var reads = new OriginalPayloadReadCounter();
        await using var context = database.Context(null, reads);
        var principal = await database.AuthenticateAsync(context);
        var privacy = database.Privacy.Authority(context, database.Clock);
        await privacy.ExecuteSerializedAsync(async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, token);
            var authority = await database.MaintenanceAuthority(context, principal)
                .AcquireForEffectAsync(id, claimed.LeaseId!.Value, claimed.Revision, privacy, token);
            if (expired)
            {
                await Assert.That(authority).IsNull();
                await Assert.That(reads.PayloadReads).IsEqualTo(0);
            }
            else
            {
                await Assert.That(authority).IsNotNull();
                await Assert.That(reads.PayloadReads).IsEqualTo(1);
            }
            return true;
        }, CancellationToken.None);
        var retained = await context.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == id);
        await Assert.That(retained.CompletedAt).IsNull();
        await Assert.That(retained.LeaseExpiresAt).IsEqualTo(Now.AddMinutes(2));
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(3, false)]
    [Arguments(4, false)]
    [Arguments(5, false)]
    [Arguments(6, false)]
    [Arguments(7, false)]
    [Arguments(8, false)]
    [Arguments(0, true)]
    [Arguments(3, true)]
    [Arguments(5, true)]
    public async Task NativeEffectPurposeRequiresItsActualCurrentLeaseAndOriginalWriter(
        int substitution, bool instanceScoped)
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(capture: "inert", instanceScoped: instanceScoped);
        ModerationEffectIntent claimed;
        await using (var claimant = database.Context())
        {
            var machine = await database.AuthenticateAsync(claimant);
            var claim = await database.Repository(claimant, machine).ClaimAsync(id, 1);
            await Assert.That(claim.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Known);
            claimed = claim.Effect!;
        }
        var tenantId = substitution == 5 ? database.OtherTenantId : database.TenantId;
        var reads = new OriginalPayloadReadCounter();
        await using var context = database.Context(tenantId, reads);
        var machineContext = await database.AuthenticateAsync(context, tenantId);
        if (substitution == 3) database.Clock.UtcNow = claimed.LeaseExpiresAt!.Value;
        if (substitution is 4 or 6 or 7)
        {
            await using var writer = database.Context();
            writer.ModerationTrustDomainId = database.Instance.TrustDomainId;
            if (substitution == 4)
                writer.ModerationOperationReceipts.Remove(
                    await writer.ModerationOperationReceipts.SingleAsync(row => row.Id == claimed.OperationId));
            else if (substitution == 6)
                writer.ModerationNoticeSources.Remove(
                    await writer.ModerationNoticeSources.SingleAsync(row => row.EffectId == id));
            else
                await writer.ExternalApiKeys.Where(row => row.KeyId == database.KeyId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAt, Now.UtcDateTime));
            await writer.SaveChangesAsync();
        }
        var privacy = database.Privacy.Authority(context, database.Clock);
        await privacy.ExecuteSerializedAsync(async token =>
        {
            await using var transaction = substitution == 8 ? null
                : await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
            var authority = await database.MaintenanceAuthority(context, machineContext).AcquireForEffectAsync(
                id, substitution == 1 ? Guid.CreateVersion7() : claimed.LeaseId!.Value,
                substitution == 2 ? claimed.Revision + 1 : claimed.Revision, privacy, token);
            if (substitution == 0)
            {
                await Assert.That(authority).IsNotNull();
                await Assert.That(authority!.UserId).IsNotEqualTo(database.OwnerId);
                await Assert.That(authority.Binding.MaintenanceServicePrincipalId).IsEqualTo(authority.UserId);
            }
            else
            {
                await Assert.That(authority).IsNull();
                await Assert.That(reads.PayloadReads).IsEqualTo(0);
            }
            return true;
        }, CancellationToken.None);
        await using var unchanged = database.Context();
        unchanged.ModerationTrustDomainId = database.Instance.TrustDomainId;
        var retained = await unchanged.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == id);
        await Assert.That(retained.LeaseId).IsEqualTo(claimed.LeaseId);
        await Assert.That(retained.Revision).IsEqualTo(claimed.Revision);
        await Assert.That(retained.CompletedAt).IsNull();
    }
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Recovery_drains_an_original_inert_effect_when_its_wake_up_is_missing_or_completed(
        bool completedWakeUp, bool instanceScoped)
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(capture: "inert", instanceScoped: instanceScoped);
        if (completedWakeUp)
        {
            await using var writer = database.Context();
            var message = ModerationEffectOutboxMessageFactory.Create(id, Now);
            message.Status = OutboxMessageStatus.Completed;
            message.ProcessedAt = Now.UtcDateTime;
            writer.OutboxMessages.Add(message);
            await writer.SaveChangesAsync();
        }
        var tenants = new List<DrainTenant>();
        var machines = new List<MachinePrincipalAccessor>();
        await using var services = database.DrainServices(database.TenantId, tenants, machines);
        var recovery = services.GetRequiredService<ModerationEffectRecoveryService>();
        var cursor = await recovery.ExecutePassAsync(database.TenantId, 1, null, default);
        await Assert.That(cursor).IsEqualTo(id);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
        var retained = await reader.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == id);
        await Assert.That(retained.CompletedAt).IsEqualTo(Now);
        await Assert.That(retained.Revision).IsEqualTo(3);
        await Assert.That(await recovery.ExecutePassAsync(database.TenantId, 1, cursor, default)).IsNull();
        await Assert.That(await reader.OutboxMessages.CountAsync()).IsEqualTo(completedWakeUp ? 1 : 0);
        if (completedWakeUp)
            await Assert.That((await reader.OutboxMessages.SingleAsync()).Status).IsEqualTo(OutboxMessageStatus.Completed);
        foreach (var tenant in tenants) await Assert.That(tenant.TenantId).IsNull();
        foreach (var machine in machines) await Assert.That(machine.Current).IsNull();
    }

    [Test]
    [Arguments(OutboxMessageStatus.Pending)]
    [Arguments(OutboxMessageStatus.Processing)]
    [Arguments(OutboxMessageStatus.Failed)]
    [Arguments(OutboxMessageStatus.DeadLettered)]
    public async Task Recovery_does_not_take_over_an_owned_retry_or_operator_dead_letter(OutboxMessageStatus status)
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(capture: "inert");
        await using (var writer = database.Context())
        {
            var message = ModerationEffectOutboxMessageFactory.Create(id, Now);
            message.Status = status;
            writer.OutboxMessages.Add(message);
            await writer.SaveChangesAsync();
        }
        var tenants = new List<DrainTenant>();
        var machines = new List<MachinePrincipalAccessor>();
        await using var services = database.DrainServices(database.TenantId, tenants, machines);
        await Assert.That(await services.GetRequiredService<ModerationEffectRecoveryService>()
            .ExecutePassAsync(database.TenantId, 1, null, default)).IsNull();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
        var retained = await reader.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == id);
        await Assert.That(retained.CompletedAt).IsNull();
        await Assert.That(retained.LeaseId).IsNull();
        await Assert.That(retained.Revision).IsEqualTo(1);
        await Assert.That((await reader.OutboxMessages.SingleAsync()).Status).IsEqualTo(status);
        foreach (var tenant in tenants) await Assert.That(tenant.TenantId).IsNull();
        foreach (var machine in machines) await Assert.That(machine.Current).IsNull();
    }

    [Test]
    public async Task Recovery_cursor_passes_unavailable_substantive_work_without_acknowledging_it()
    {
        await using var database = await Database.CreateAsync();
        var blocked = await database.SeedAsync(capture: "substantive",
            effectId: Guid.Parse("01999999-0000-7000-8000-000000000001"));
        var ready = await database.SeedAsync(capture: "inert",
            effectId: Guid.Parse("01999999-0000-7000-8000-000000000002"));
        var tenants = new List<DrainTenant>();
        var machines = new List<MachinePrincipalAccessor>();
        await using var services = database.DrainServices(database.TenantId, tenants, machines);
        var recovery = services.GetRequiredService<ModerationEffectRecoveryService>();
        var first = await recovery.ExecutePassAsync(database.TenantId, 1, null, default);
        await Assert.That(first).IsEqualTo(blocked);
        var second = await recovery.ExecutePassAsync(database.TenantId, 1, first, default);
        await Assert.That(second).IsEqualTo(ready);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
        var untouched = await reader.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == blocked);
        var completed = await reader.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == ready);
        await Assert.That(untouched.CompletedAt).IsNull();
        await Assert.That(untouched.LeaseId).IsNull();
        await Assert.That(untouched.Revision).IsEqualTo(1);
        await Assert.That(completed.CompletedAt).IsEqualTo(Now);
        await Assert.That(completed.Revision).IsEqualTo(3);
        foreach (var tenant in tenants) await Assert.That(tenant.TenantId).IsNull();
        foreach (var machine in machines) await Assert.That(machine.Current).IsNull();
    }

    [Test]
    public async Task Recovery_waits_for_the_actual_lease_deadline_before_reclaiming_an_orphaned_intent()
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(capture: "inert");
        await using (var context = database.Context())
        {
            var machine = await database.AuthenticateAsync(context);
            await Assert.That((await database.Repository(context, machine).ClaimAsync(id, 1)).Outcome)
                .IsEqualTo(ModerationEffectLeaseOutcome.Known);
        }
        var tenants = new List<DrainTenant>();
        var machines = new List<MachinePrincipalAccessor>();
        await using var services = database.DrainServices(database.TenantId, tenants, machines);
        var recovery = services.GetRequiredService<ModerationEffectRecoveryService>();
        await Assert.That(await recovery.ExecutePassAsync(database.TenantId, 1, null, default)).IsNull();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
        var claimed = await reader.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == id);
        await Assert.That(claimed.CompletedAt).IsNull();
        await Assert.That(claimed.Revision).IsEqualTo(2);
        database.Clock.UtcNow = claimed.LeaseExpiresAt!.Value;
        await Assert.That(await recovery.ExecutePassAsync(database.TenantId, 1, null, default)).IsEqualTo(id);
        var recovered = await reader.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == id);
        await Assert.That(recovered.CompletedAt).IsEqualTo(database.Clock.UtcNow);
        await Assert.That(recovered.Revision).IsEqualTo(4);
        await Assert.That(recovered.LeaseId).IsNotEqualTo(claimed.LeaseId);
        foreach (var tenant in tenants) await Assert.That(tenant.TenantId).IsNull();
        foreach (var machine in machines) await Assert.That(machine.Current).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Recovery_in_another_native_tenant_cannot_discover_or_claim_the_original_intent(bool instanceScoped)
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(capture: "inert", instanceScoped: instanceScoped);
        var tenants = new List<DrainTenant>();
        var machines = new List<MachinePrincipalAccessor>();
        await using var services = database.DrainServices(database.OtherTenantId, tenants, machines);
        await Assert.That(await services.GetRequiredService<ModerationEffectRecoveryService>()
            .ExecutePassAsync(database.OtherTenantId, 1, null, default)).IsNull();
        await using (var foreign = database.Context(database.OtherTenantId))
        {
            var machine = await database.AuthenticateAsync(foreign, database.OtherTenantId);
            await Assert.That((await database.Repository(foreign, machine).ClaimAsync(id, 1)).Outcome)
                .IsEqualTo(ModerationEffectLeaseOutcome.NotFound);
        }
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
        var retained = await reader.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == id);
        await Assert.That(retained.CompletedAt).IsNull();
        await Assert.That(retained.LeaseId).IsNull();
        await Assert.That(retained.Revision).IsEqualTo(1);
        foreach (var tenant in tenants) await Assert.That(tenant.TenantId).IsNull();
        foreach (var machine in machines) await Assert.That(machine.Current).IsNull();
    }

    [Test]
    public async Task Actual_native_outbox_dispatch_completes_original_inert_work_and_verifies_duplicate_after_expiry()
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(capture: "inert");
        var tenants = new List<DrainTenant>();
        var machines = new List<MachinePrincipalAccessor>();
        await using var services = database.DrainServices(database.TenantId, tenants, machines);
        var dispatcher = services.GetRequiredService<ModerationEffectOutboxDispatcher>();
        var message = new OutboxMessage
        {
            Id = id,
            AggregateId = id,
            AggregateType = ModerationEffectOutboxMessageFactory.AggregateType,
            EventType = ModerationEffectOutboxMessageFactory.EventType
        };
        await dispatcher.DispatchAsync(message);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
        var completed = await reader.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == id);
        await Assert.That(completed.CompletedAt).IsEqualTo(Now);
        await Assert.That(completed.Revision).IsEqualTo(3);
        database.Clock.UtcNow = Now.AddMinutes(3);
        await dispatcher.DispatchAsync(message);
        var retained = await reader.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == id);
        await Assert.That(retained.Revision).IsEqualTo(completed.Revision);
        await Assert.That(retained.LeaseId).IsEqualTo(completed.LeaseId);
        await Assert.That(retained.CompletedAt).IsEqualTo(completed.CompletedAt);
        await Assert.That(tenants.Count).IsEqualTo(2);
        foreach (var tenant in tenants) await Assert.That(tenant.TenantId).IsNull();
        foreach (var machine in machines) await Assert.That(machine.Current).IsNull();
    }

    [Test]
    [Arguments("missing", false)]
    [Arguments("substantive", false)]
    [Arguments("hash", true)]
    [Arguments("substantive", true)]
    [Arguments("foreign-tenant", false)]
    public async Task Actual_native_dispatch_cannot_acknowledge_unproved_or_foreign_work(string source, bool completed)
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(completed: completed, capture: source == "foreign-tenant" ? "inert" : source);
        var tenants = new List<DrainTenant>();
        var machines = new List<MachinePrincipalAccessor>();
        await using var services = database.DrainServices(
            source == "foreign-tenant" ? database.OtherTenantId : database.TenantId, tenants, machines);
        var dispatcher = services.GetRequiredService<ModerationEffectOutboxDispatcher>();
        var message = new OutboxMessage
        {
            Id = id,
            AggregateId = id,
            AggregateType = ModerationEffectOutboxMessageFactory.AggregateType,
            EventType = ModerationEffectOutboxMessageFactory.EventType
        };
        await Assert.That(() => dispatcher.DispatchAsync(message)).Throws<InvalidOperationException>();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
        var retained = await reader.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == id);
        await Assert.That(retained.Revision).IsEqualTo(completed ? 3 : 1);
        await Assert.That(retained.CompletedAt is not null).IsEqualTo(completed);
        await Assert.That(tenants.Single().TenantId).IsNull();
        await Assert.That(machines.Single().Current).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Failed_inert_dispatch_releases_only_its_claim_and_can_retry_without_waiting_for_expiry(bool afterFlush)
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(capture: "inert");
        var tenants = new List<DrainTenant>();
        var machines = new List<MachinePrincipalAccessor>();
        var rejection = new RejectInertCompletion(afterFlush);
        await using var services = database.DrainServices(database.TenantId, tenants, machines, rejection);
        var dispatcher = services.GetRequiredService<ModerationEffectOutboxDispatcher>();
        var message = ModerationEffectOutboxMessageFactory.Create(id, Now.AddHours(-1));
        await Assert.That(() => dispatcher.DispatchAsync(message)).Throws<InvalidOperationException>();
        await Assert.That(rejection.Injected).IsTrue();
        await using (var reader = database.Context())
        {
            var repository = database.Repository(reader, await database.AuthenticateAsync(reader));
            var released = await repository.ReadWithoutSubstantiveWorkAsync(id);
            await Assert.That(released.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Known);
            await Assert.That(released.Effect!.Revision).IsEqualTo(3);
            await Assert.That(released.Effect.LeaseId).IsNull();
            await Assert.That(released.Effect.CompletedAt).IsNull();
        }
        await Assert.That(database.Clock.UtcNow).IsEqualTo(Now);
        await dispatcher.DispatchAsync(message);
        await using (var reader = database.Context())
        {
            var completed = await database.Repository(reader, await database.AuthenticateAsync(reader))
                .ReadWithoutSubstantiveWorkAsync(id);
            await Assert.That(completed.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Completed);
            await Assert.That(completed.Effect!.Revision).IsEqualTo(5);
            await Assert.That(completed.Effect.CompletedAt).IsEqualTo((DateTimeOffset?)Now);
        }
        foreach (var tenant in tenants) await Assert.That(tenant.TenantId).IsNull();
        foreach (var machine in machines) await Assert.That(machine.Current).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task One_recovery_page_continues_after_owned_completion_rolls_back_and_retries_on_wrap(bool afterFlush)
    {
        await using var database = await Database.CreateAsync();
        var rejectedId = await database.SeedAsync(capture: "inert",
            effectId: Guid.Parse("01999999-0000-7000-8000-000000000001"));
        var readyId = await database.SeedAsync(capture: "inert",
            effectId: Guid.Parse("01999999-0000-7000-8000-000000000002"));
        var tenants = new List<DrainTenant>();
        var machines = new List<MachinePrincipalAccessor>();
        var rejection = new RejectInertCompletion(afterFlush);
        await using var services = database.DrainServices(database.TenantId, tenants, machines, rejection);
        var recovery = services.GetRequiredService<ModerationEffectRecoveryService>();

        await Assert.That(await recovery.ExecutePassAsync(database.TenantId, 2, null, default)).IsEqualTo(readyId);
        await Assert.That(rejection.Injected).IsTrue();
        await using (var reader = database.Context())
        {
            reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
            var rejected = await reader.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == rejectedId);
            var completed = await reader.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == readyId);
            await Assert.That(rejected.CompletedAt).IsNull();
            await Assert.That(rejected.LeaseId).IsNull();
            await Assert.That(rejected.Revision).IsEqualTo(3L);
            await Assert.That(completed.CompletedAt).IsEqualTo(Now);
            await Assert.That(completed.Revision).IsEqualTo(3L);
            await Assert.That(await reader.OutboxMessages.CountAsync()).IsEqualTo(0);
        }

        await Assert.That(await recovery.ExecutePassAsync(database.TenantId, 2, readyId, default)).IsNull();
        await Assert.That(database.Clock.UtcNow).IsEqualTo(Now);
        await Assert.That(await recovery.ExecutePassAsync(database.TenantId, 2, null, default)).IsEqualTo(rejectedId);
        await using (var reader = database.Context())
        {
            reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
            var recovered = await reader.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == rejectedId);
            var completed = await reader.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == readyId);
            await Assert.That(recovered.CompletedAt).IsEqualTo(Now);
            await Assert.That(recovered.Revision).IsEqualTo(5L);
            await Assert.That(completed.CompletedAt).IsEqualTo(Now);
            await Assert.That(completed.Revision).IsEqualTo(3L);
            await Assert.That(await reader.OutboxMessages.CountAsync()).IsEqualTo(0);
        }
        foreach (var tenant in tenants) await Assert.That(tenant.TenantId).IsNull();
        foreach (var machine in machines) await Assert.That(machine.Current).IsNull();
    }

    [Test]
    public async Task Original_inert_capture_completes_exact_lease_despite_current_decision_and_epoch_advancing()
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(advanceDecision: true, capture: "inert");
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Instance.TrustDomainId;
            var decision = await writer.ModerationDecisions.SingleAsync();
            decision.AcceptFinding(Guid.CreateVersion7(), 2, Now.AddMinutes(-50));
            await writer.SaveChangesAsync();
            await writer.ModerationQualificationEpochs.Where(row => row.TenantId == database.TenantId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, 2));
        }
        await using var context = database.Context();
        var repository = database.Repository(context, await database.AuthenticateAsync(context));
        var claim = await repository.ClaimAsync(id, 1);
        var result = await repository.CompleteWithoutSubstantiveWorkAsync(id, claim.Effect!.LeaseId!.Value, 2);
        await Assert.That(result.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Completed);
        await Assert.That(result.Effect!.Revision).IsEqualTo(3);
        await Assert.That(result.Effect.CompletedAt).IsEqualTo((DateTimeOffset?)Now);
        await Assert.That(result.Effect.LeaseId).IsEqualTo(claim.Effect.LeaseId);
        await Assert.That(result.Effect.DecisionRevision).IsEqualTo(1);
        await Assert.That(result.Effect.QualificationRevision).IsEqualTo(1);
        await Assert.That((await repository.CompleteWithoutSubstantiveWorkAsync(id, claim.Effect.LeaseId.Value, 2)).Outcome)
            .IsEqualTo(ModerationEffectLeaseOutcome.Conflict);
        var retained = await repository.ReadAsync(id);
        await Assert.That(retained.Effect!.Revision).IsEqualTo(3);
        await Assert.That(retained.Effect.CompletedAt).IsEqualTo(result.Effect.CompletedAt);
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        context.ModerationTrustDomainId = database.Instance.TrustDomainId;
        await Assert.That(await context.ModerationNotices.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments("missing")]
    [Arguments("substantive")]
    [Arguments("hash")]
    [Arguments("malformed")]
    [Arguments("binding")]
    [Arguments("bounds")]
    [Arguments("version")]
    [Arguments("tenant")]
    [Arguments("host")]
    [Arguments("authority")]
    [Arguments("ancestry")]
    [Arguments("subject-origin")]
    [Arguments("source-tenant")]
    [Arguments("qualification")]
    public async Task Missing_invalid_or_substantive_original_capture_preserves_current_ownership(string capture)
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(capture: capture);
        await using var context = database.Context();
        var repository = database.Repository(context, await database.AuthenticateAsync(context));
        var claim = await repository.ClaimAsync(id, 1);
        var result = await repository.CompleteWithoutSubstantiveWorkAsync(id, claim.Effect!.LeaseId!.Value, 2);
        await Assert.That(result.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Unavailable);
        await Assert.That(result.UnavailableReason).IsEqualTo(capture == "missing"
            ? ModerationEffectLeaseUnavailableReason.MissingOriginalCapture
            : ModerationEffectLeaseUnavailableReason.InvalidOrSubstantiveOriginalCapture);
        var retained = await repository.ReadAsync(id);
        await Assert.That(retained.Effect!.Revision).IsEqualTo(2);
        await Assert.That(retained.Effect.LeaseId).IsEqualTo(claim.Effect.LeaseId);
        await Assert.That(retained.Effect.LeaseExpiresAt).IsEqualTo(claim.Effect.LeaseExpiresAt);
        await Assert.That(retained.Effect.CompletedAt).IsNull();
    }

    [Test]
    public async Task Expired_stale_and_old_owner_completion_never_changes_a_successor()
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(capture: "inert");
        await using var context = database.Context();
        var repository = database.Repository(context, await database.AuthenticateAsync(context));
        var claim = await repository.ClaimAsync(id, 1);
        await Assert.That((await repository.CompleteWithoutSubstantiveWorkAsync(id, claim.Effect!.LeaseId!.Value, 1)).Outcome)
            .IsEqualTo(ModerationEffectLeaseOutcome.Conflict);
        database.Clock.UtcNow = claim.Effect.LeaseExpiresAt!.Value;
        var expired = await repository.CompleteWithoutSubstantiveWorkAsync(id, claim.Effect.LeaseId.Value, 2);
        await Assert.That(expired.UnavailableReason).IsEqualTo(ModerationEffectLeaseUnavailableReason.LeaseWindowElapsed);
        var successor = await repository.ClaimAsync(id, 2);
        await Assert.That((await repository.CompleteWithoutSubstantiveWorkAsync(id, claim.Effect.LeaseId.Value, 2)).Outcome)
            .IsEqualTo(ModerationEffectLeaseOutcome.Conflict);
        await Assert.That((await repository.CompleteWithoutSubstantiveWorkAsync(id, claim.Effect.LeaseId.Value, 3)).Outcome)
            .IsEqualTo(ModerationEffectLeaseOutcome.Conflict);
        var retained = await repository.ReadAsync(id);
        await Assert.That(retained.Effect!.Revision).IsEqualTo(3);
        await Assert.That(retained.Effect.LeaseId).IsEqualTo(successor.Effect!.LeaseId);
        await Assert.That(retained.Effect.CompletedAt).IsNull();
    }

    [Test]
    public async Task Completion_expiring_during_final_flush_rolls_back_without_releasing_ownership()
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(capture: "inert");
        ModerationEffectLeaseResult claim;
        await using (var claimant = database.Context())
            claim = await database.Repository(claimant, await database.AuthenticateAsync(claimant)).ClaimAsync(id, 1);
        var expiry = new ExpireClaimOnSave(database.Clock);
        await using var context = database.Context(null, expiry);
        var repository = database.Repository(context, await database.AuthenticateAsync(context));
        var result = await repository.CompleteWithoutSubstantiveWorkAsync(id, claim.Effect!.LeaseId!.Value, 2);
        await Assert.That(expiry.Observed).IsTrue();
        await Assert.That(result.UnavailableReason).IsEqualTo(ModerationEffectLeaseUnavailableReason.LeaseWindowElapsed);
        var retained = await repository.ReadAsync(id);
        await Assert.That(retained.Effect!.Revision).IsEqualTo(2);
        await Assert.That(retained.Effect.LeaseId).IsEqualTo(claim.Effect.LeaseId);
        await Assert.That(retained.Effect.CompletedAt).IsNull();
    }

    [Test]
    [Arguments("current")]
    [Arguments("lease-expiry")]
    [Arguments("credential-revocation")]
    public async Task InertCompletionRechecksItsExactLeaseAndCredentialAfterDisclosureFinalization(string boundary)
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(capture: "inert");
        ModerationEffectLeaseResult claim;
        long? originalEpoch;
        await using (var claimant = database.Context())
        {
            claim = await database.Repository(claimant, await database.AuthenticateAsync(claimant)).ClaimAsync(id, 1);
            originalEpoch = await claimant.Set<EventDiscoveryRevision>().AsNoTracking()
                .Where(row => row.TenantId == database.TenantId)
                .Select(row => (long?)row.DisclosureEpoch).SingleOrDefaultAsync();
        }
        var advancement = new InertDisclosureAdvancement(database.TenantId, originalEpoch, async current =>
        {
            if (boundary == "lease-expiry")
                database.Clock.UtcNow = claim.Effect!.LeaseExpiresAt!.Value;
            else if (boundary == "credential-revocation")
                await Assert.That(await current.ExternalApiKeys.Where(row => row.KeyId == database.KeyId)
                    .ExecuteUpdateAsync(update => update.SetProperty(
                        row => row.ExternalApiKeyStatusId, (int)ExternalApiKeyStatusEnum.Revoked))).IsEqualTo(1);
        });
        var saved = new EnlistInertCompletionDisclosure(database.TenantId, advancement);
        await using var context = database.Context(null, saved, advancement);
        var repository = database.Repository(context, await database.AuthenticateAsync(context));

        var result = await repository.CompleteWithoutSubstantiveWorkAsync(id, claim.Effect!.LeaseId!.Value, 2);

        await Assert.That(saved.ObservedCompletion).IsTrue();
        await Assert.That(advancement.ObservedAdvancement).IsTrue();
        await Assert.That(result.Outcome).IsEqualTo(boundary switch
        {
            "current" => ModerationEffectLeaseOutcome.Completed,
            "lease-expiry" => ModerationEffectLeaseOutcome.Unavailable,
            _ => ModerationEffectLeaseOutcome.Denied
        });
        if (boundary == "lease-expiry")
            await Assert.That(result.UnavailableReason).IsEqualTo(ModerationEffectLeaseUnavailableReason.LeaseWindowElapsed);
        var retained = await repository.ReadAsync(id);
        await Assert.That(retained.Effect).IsNotNull();
        await Assert.That(retained.Effect!.Revision).IsEqualTo(boundary == "current" ? 3L : 2L);
        await Assert.That(retained.Effect.LeaseId).IsEqualTo(claim.Effect.LeaseId);
        await Assert.That(retained.Effect.LeaseStartedAt).IsEqualTo(claim.Effect.LeaseStartedAt);
        await Assert.That(retained.Effect.LeaseExpiresAt).IsEqualTo(claim.Effect.LeaseExpiresAt);
        await Assert.That(retained.Effect.CompletedAt).IsEqualTo(boundary == "current" ? (DateTimeOffset?)Now : null);
        var committedEpoch = await context.Set<EventDiscoveryRevision>().AsNoTracking()
            .Where(row => row.TenantId == database.TenantId)
            .Select(row => (long?)row.DisclosureEpoch).SingleOrDefaultAsync();
        await Assert.That(committedEpoch).IsEqualTo(boundary == "current"
            ? originalEpoch.GetValueOrDefault() + 1 : originalEpoch);
        await Assert.That((await context.ExternalApiKeys.AsNoTracking()
            .SingleAsync(row => row.KeyId == database.KeyId)).ExternalApiKeyStatusId)
            .IsEqualTo((int)ExternalApiKeyStatusEnum.Active);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Lost_completion_acknowledgement_verification_or_replay_recognizes_only_this_invocation(bool verify)
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(capture: "inert");
        ModerationEffectLeaseResult claim;
        await using (var claimant = database.Context())
            claim = await database.Repository(claimant, await database.AuthenticateAsync(claimant)).ClaimAsync(id, 1);
        var fault = new LostAcknowledgement(database.Clock, verify, () =>
        {
            database.Clock.UtcNow = claim.Effect!.LeaseExpiresAt!.Value;
            return Task.CompletedTask;
        });
        await using var context = database.Context(interceptors: [fault]);
        var repository = database.Repository(context, await database.AuthenticateAsync(context));
        var result = await repository.CompleteWithoutSubstantiveWorkAsync(id, claim.Effect!.LeaseId!.Value, 2);
        await Assert.That(fault.Injected).IsTrue();
        await Assert.That(result.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Completed);
        await Assert.That(result.Effect!.Revision).IsEqualTo(3);
        await Assert.That(result.Effect.LeaseId).IsEqualTo(claim.Effect.LeaseId);
        await Assert.That(result.Effect.CompletedAt).IsEqualTo((DateTimeOffset?)Now);
        var retained = await repository.ReadAsync(id);
        await Assert.That(retained.Effect!.CompletedAt).IsEqualTo(result.Effect.CompletedAt);
        await Assert.That(retained.Effect.Revision).IsEqualTo(3);
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(false, false)]
    [Arguments(true, true)]
    [Arguments(false, true)]
    public async Task Ambiguous_uncommitted_completion_cannot_accept_or_alter_a_successor(bool verify, bool completeSuccessor)
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(capture: "inert");
        ModerationEffectLeaseResult claim;
        await using (var claimant = database.Context())
            claim = await database.Repository(claimant, await database.AuthenticateAsync(claimant)).ClaimAsync(id, 1);
        ModerationEffectLeaseResult? successor = null;
        var fault = new LostCompletionBeforeCommit(verify, async () =>
        {
            database.Clock.UtcNow = claim.Effect!.LeaseExpiresAt!.Value;
            await using var recovery = database.Context();
            var repository = database.Repository(recovery, await database.AuthenticateAsync(recovery));
            successor = await repository.ClaimAsync(id, 2);
            if (completeSuccessor)
                successor = await repository.CompleteWithoutSubstantiveWorkAsync(id, successor.Effect!.LeaseId!.Value, 3);
        });
        await using var context = database.Context(interceptors: [fault]);
        var repository = database.Repository(context, await database.AuthenticateAsync(context));
        var result = await repository.CompleteWithoutSubstantiveWorkAsync(id, claim.Effect!.LeaseId!.Value, 2);
        await Assert.That(fault.Injected).IsTrue();
        await Assert.That(successor!.Outcome).IsEqualTo(completeSuccessor
            ? ModerationEffectLeaseOutcome.Completed : ModerationEffectLeaseOutcome.Known);
        await Assert.That(result.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Conflict);
        var retained = await repository.ReadAsync(id);
        await Assert.That(retained.Effect!.Revision).IsEqualTo(completeSuccessor ? 4L : 3L);
        await Assert.That(retained.Effect.LeaseId).IsEqualTo(successor.Effect!.LeaseId);
        await Assert.That(retained.Effect.CompletedAt).IsEqualTo(successor.Effect.CompletedAt);
        await Assert.That(retained.Effect.LeaseId == claim.Effect.LeaseId).IsFalse();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Completion_requires_the_original_cause_receipt_without_releasing_the_lease(bool missing)
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(capture: "inert");
        await using var context = database.Context();
        var repository = database.Repository(context, await database.AuthenticateAsync(context));
        var claim = await repository.ClaimAsync(id, 1);
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Instance.TrustDomainId;
            // Retain the FK row but remove its visible original trust-domain cause.
            if (missing)
            {
                var foreignTrustDomain = Guid.CreateVersion7();
                await writer.ModerationOperationReceipts.ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.TrustDomainId, foreignTrustDomain));
            }
            else await writer.ModerationOperationReceipts.ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.PolicyRevision, 2));
        }
        var result = await repository.CompleteWithoutSubstantiveWorkAsync(id, claim.Effect!.LeaseId!.Value, 2);
        await Assert.That(result.UnavailableReason).IsEqualTo(missing
            ? ModerationEffectLeaseUnavailableReason.MissingCause : ModerationEffectLeaseUnavailableReason.InconsistentCause);
        context.ModerationTrustDomainId = database.Instance.TrustDomainId;
        var retained = await context.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == id);
        await Assert.That(retained.Revision).IsEqualTo(2);
        await Assert.That(retained.LeaseId).IsEqualTo(claim.Effect.LeaseId);
        await Assert.That(retained.CompletedAt).IsNull();
    }

    [Test]
    public async Task Claim_busy_expired_recovery_and_stale_release_never_complete_consequences()
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync();
        await using var context = database.Context();
        var machine = await database.AuthenticateAsync(context);
        var repository = database.Repository(context, machine);
        var first = await repository.ClaimAsync(id, 1);
        await Assert.That(first.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Known);
        await Assert.That(first.Effect!.Revision).IsEqualTo(2);
        await Assert.That(first.Effect.LeaseId!.Value.Version).IsEqualTo(7);
        await Assert.That(first.Effect.CompletedAt).IsNull();
        await Assert.That((await repository.ClaimAsync(id, 2)).Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Busy);
        database.Clock.UtcNow = first.Effect.LeaseExpiresAt!.Value;
        var successor = await repository.ClaimAsync(id, 2);
        await Assert.That(successor.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Known);
        await Assert.That(successor.Effect!.Revision).IsEqualTo(3);
        await Assert.That(successor.Effect.LeaseId == first.Effect.LeaseId).IsFalse();
        await Assert.That((await repository.ReleaseAsync(id, first.Effect.LeaseId.Value, 2)).Outcome)
            .IsEqualTo(ModerationEffectLeaseOutcome.Conflict);
        await Assert.That((await repository.ReleaseAsync(id, first.Effect.LeaseId.Value, 3)).Outcome)
            .IsEqualTo(ModerationEffectLeaseOutcome.Conflict);
        var current = await repository.ReadAsync(id);
        await Assert.That(current.Effect!.LeaseId).IsEqualTo(successor.Effect.LeaseId);
        database.Clock.UtcNow = successor.Effect.LeaseExpiresAt!.Value;
        var released = await repository.ReleaseAsync(id, successor.Effect.LeaseId!.Value, 3);
        await Assert.That(released.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Known);
        await Assert.That(released.Effect!.Revision).IsEqualTo(4);
        await Assert.That(released.Effect.LeaseId).IsNull();
        await Assert.That(released.Effect.CompletedAt).IsNull();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
    }

    [Test]
    public async Task Lease_expiring_during_final_flush_rolls_back_claim_and_generation()
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync();
        var expiry = new ExpireClaimOnSave(database.Clock);
        await using var context = database.Context(null, expiry);
        var repository = database.Repository(context, await database.AuthenticateAsync(context));
        var result = await repository.ClaimAsync(id, 1);
        await Assert.That(expiry.Observed).IsTrue();
        await Assert.That(result.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Unavailable);
        await Assert.That(result.UnavailableReason).IsEqualTo(ModerationEffectLeaseUnavailableReason.LeaseWindowElapsed);
        await using var observer = database.Context();
        var original = await database.Repository(observer, await database.AuthenticateAsync(observer)).ReadAsync(id);
        await Assert.That(original.Effect!.Revision).IsEqualTo(1);
        await Assert.That(original.Effect.LeaseId).IsNull();
        await Assert.That(original.Effect.CompletedAt).IsNull();
    }

    [Test]
    public async Task Original_receipt_remains_claimable_after_decision_advances_without_current_policy_reinterpretation()
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(advanceDecision: true);
        await using var context = database.Context();
        var repository = database.Repository(context, await database.AuthenticateAsync(context));
        var result = await repository.ClaimAsync(id, 1);
        await Assert.That(result.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Known);
        await Assert.That(result.Effect!.DecisionRevision).IsEqualTo(1);
        await Assert.That(result.Effect.PolicyRevision).IsEqualTo(1);
        await Assert.That(result.Effect.CompletedAt).IsNull();
    }

    [Test]
    public async Task Inconsistent_original_receipt_returns_missing_integration_evidence_without_a_lease()
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync();
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Instance.TrustDomainId;
            await writer.ModerationOperationReceipts.ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.PolicyRevision, 2));
        }
        await using var context = database.Context();
        var result = await database.Repository(context, await database.AuthenticateAsync(context)).ClaimAsync(id, 1);
        await Assert.That(result.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Unavailable);
        await Assert.That(result.UnavailableReason).IsEqualTo(ModerationEffectLeaseUnavailableReason.InconsistentCause);
        context.ModerationTrustDomainId = database.Instance.TrustDomainId;
        var effect = await context.ModerationEffectIntents.AsNoTracking().SingleAsync();
        await Assert.That(effect.Revision).IsEqualTo(1);
        await Assert.That(effect.LeaseId).IsNull();
    }

    [Test]
    public async Task Actual_foreign_tenant_cannot_read_or_claim_an_effect_in_the_same_trust_domain()
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync();
        await using var context = database.Context(database.OtherTenantId);
        var repository = database.Repository(context, await database.AuthenticateAsync(context, database.OtherTenantId));
        await Assert.That((await repository.ReadAsync(id)).Outcome).IsEqualTo(ModerationEffectLeaseOutcome.NotFound);
        await Assert.That((await repository.ClaimAsync(id, 1)).Outcome).IsEqualTo(ModerationEffectLeaseOutcome.NotFound);
        await using var local = database.Context();
        var original = await database.Repository(local, await database.AuthenticateAsync(local)).ReadAsync(id);
        await Assert.That(original.Effect!.Revision).IsEqualTo(1);
        await Assert.That(original.Effect.LeaseId).IsNull();
    }

    [Test]
    [Arguments("key-revoked")]
    [Arguments("key-expired")]
    [Arguments("binding-revoked")]
    [Arguments("service-deleted")]
    [Arguments("unregistered-service")]
    [Arguments("configured")]
    [Arguments("human")]
    public async Task Current_native_credential_and_registered_maintenance_service_are_required(string change)
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync();
        await using var context = database.Context();
        var machine = await database.AuthenticateAsync(context);
        await using (var writer = database.Context())
        {
            switch (change)
            {
                case "key-revoked":
                    var revokedKey = await new ExternalApiKeyAuthenticationRepository(
                        new SecurityTestSupport.AdministrativeAuthorityTestFactory(writer)).GetByKeyIdForAuthentication(database.KeyId);
                    writer.Attach(revokedKey!);
                    revokedKey!.ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Revoked;
                    await writer.SaveChangesAsync();
                    break;
                case "key-expired":
                    var expiredKey = await new ExternalApiKeyAuthenticationRepository(
                        new SecurityTestSupport.AdministrativeAuthorityTestFactory(writer)).GetByKeyIdForAuthentication(database.KeyId);
                    writer.Attach(expiredKey!);
                    expiredKey!.ExpiresAt = database.Clock.UtcNow.UtcDateTime;
                    await writer.SaveChangesAsync();
                    break;
                case "binding-revoked":
                    await writer.ModerationMachineSubjectBindings.ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.RevokedAt, (DateTimeOffset?)Now));
                    break;
                case "service-deleted":
                    await writer.ServicePrincipals.ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
                    break;
                case "unregistered-service":
                    var other = new ServicePrincipal
                    {
                        Id = Guid.CreateVersion7(),
                        Code = $"other-{Guid.CreateVersion7():N}",
                        DisplayName = "Other service"
                    };
                    writer.ServicePrincipals.Add(other);
                    await writer.SaveChangesAsync();
                    await writer.ModerationHostBindings.ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.MaintenanceServicePrincipalId, other.Id));
                    break;
                case "configured":
                    machine.SetPrincipal(machine.Current! with { CredentialSource = ApiKeyCredentialSource.Configured });
                    break;
                case "human":
                    machine.Clear();
                    break;
            }
        }
        var result = await database.Repository(context, machine).ClaimAsync(id, 1);
        await Assert.That(result.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Denied);
        await Assert.That(result.Effect).IsNull();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        context.ModerationTrustDomainId = database.Instance.TrustDomainId;
        await Assert.That((await context.ModerationEffectIntents.AsNoTracking().SingleAsync()).Revision).IsEqualTo(1);
    }

    [Test]
    public async Task Completed_effect_is_observed_but_cannot_be_reclaimed_or_released()
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(completed: true);
        await using var context = database.Context();
        var repository = database.Repository(context, await database.AuthenticateAsync(context));
        var read = await repository.ReadAsync(id);
        await Assert.That(read.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Completed);
        await Assert.That((await repository.ClaimAsync(id, read.Effect!.Revision)).Outcome)
            .IsEqualTo(ModerationEffectLeaseOutcome.Completed);
        await Assert.That((await repository.ReleaseAsync(id, read.Effect.LeaseId!.Value, read.Effect.Revision)).Outcome)
            .IsEqualTo(ModerationEffectLeaseOutcome.Completed);
    }

    [Test]
    [Arguments("claim-before-cause")]
    [Arguments("zero-window")]
    [Arguments("negative-window")]
    [Arguments("complete-before-start")]
    [Arguments("complete-at-expiry")]
    public async Task Persisted_lease_chronology_cannot_rewrite_valid_completed_ownership(string change)
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync(completed: true);
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Instance.TrustDomainId;
        var original = await context.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == id);
        var started = original.LeaseStartedAt!.Value;
        var expires = original.LeaseExpiresAt!.Value;
        var completed = original.CompletedAt!.Value;
        switch (change)
        {
            case "claim-before-cause":
                started = original.CreatedAt.AddSeconds(-1);
                break;
            case "zero-window":
                expires = started;
                break;
            case "negative-window":
                expires = started.AddSeconds(-1);
                break;
            case "complete-before-start":
                completed = started.AddSeconds(-1);
                break;
            case "complete-at-expiry":
                completed = expires;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }

        var rejected = false;
        try
        {
            await context.ModerationEffectIntents.Where(row => row.Id == id).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.LeaseStartedAt, started)
                    .SetProperty(row => row.LeaseExpiresAt, expires)
                    .SetProperty(row => row.CompletedAt, completed));
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            rejected = true;
        }
        await Assert.That(rejected).IsTrue();
        var retained = await context.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == id);
        await Assert.That(retained.Revision).IsEqualTo(original.Revision);
        await Assert.That(retained.LeaseId).IsEqualTo(original.LeaseId);
        await Assert.That(retained.LeaseStartedAt).IsEqualTo(original.LeaseStartedAt);
        await Assert.That(retained.LeaseExpiresAt).IsEqualTo(original.LeaseExpiresAt);
        await Assert.That(retained.CompletedAt).IsEqualTo(original.CompletedAt);
    }

    [Test]
    public async Task Caller_owned_transaction_and_tracked_state_are_not_adopted_or_cleared()
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync();
        await using var context = database.Context();
        var repository = database.Repository(context, await database.AuthenticateAsync(context));
        await using var transaction = await context.Database.BeginTransactionAsync();
        var tenant = await context.Tenants.SingleAsync(row => row.Id == database.TenantId);
        var result = await repository.ClaimAsync(id, 1);
        await Assert.That(result.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Unavailable);
        await Assert.That(context.Database.CurrentTransaction).IsEqualTo(transaction);
        await Assert.That(context.Entry(tenant).State).IsEqualTo(EntityState.Unchanged);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Lost_commit_acknowledgement_verification_or_replay_preserves_exact_lease_and_expiry(bool verify)
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync();
        var fault = new LostAcknowledgement(database.Clock, verify);
        await using var context = database.Context(interceptors: [fault]);
        var repository = database.Repository(context, await database.AuthenticateAsync(context));
        var result = await repository.ClaimAsync(id, 1);
        await Assert.That(fault.Injected).IsTrue();
        await Assert.That(result.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Known);
        await Assert.That(result.Effect!.LeaseId).IsEqualTo(fault.CommittedLeaseId);
        await Assert.That(result.Effect.Revision).IsEqualTo(2);
        await Assert.That(result.Effect.LeaseStartedAt).IsEqualTo((DateTimeOffset?)Now);
        await Assert.That(result.Effect.LeaseExpiresAt).IsEqualTo((DateTimeOffset?)Now.AddMinutes(2));
        var read = await repository.ReadAsync(id);
        await Assert.That(read.Effect!.LeaseId).IsEqualTo(fault.CommittedLeaseId);
        await Assert.That(read.Effect.Revision).IsEqualTo(2);
        await Assert.That(read.Effect.LeaseExpiresAt).IsEqualTo(result.Effect.LeaseExpiresAt);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Lost_release_acknowledgement_verification_or_replay_preserves_exact_released_generation(bool verify)
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync();
        ModerationEffectLeaseResult claim;
        await using (var claimant = database.Context())
            claim = await database.Repository(claimant, await database.AuthenticateAsync(claimant)).ClaimAsync(id, 1);
        var fault = new LostAcknowledgement(database.Clock, verify);
        await using var context = database.Context(interceptors: [fault]);
        var repository = database.Repository(context, await database.AuthenticateAsync(context));
        var result = await repository.ReleaseAsync(id, claim.Effect!.LeaseId!.Value, 2);
        await Assert.That(fault.Injected).IsTrue();
        await Assert.That(result.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Known);
        await Assert.That(result.Effect!.Revision).IsEqualTo(3);
        await Assert.That(result.Effect.LeaseId).IsNull();
        await Assert.That(result.Effect.CompletedAt).IsNull();
        var current = await repository.ReadAsync(id);
        await Assert.That(current.Effect!.Revision).IsEqualTo(3);
        await Assert.That(current.Effect.LeaseId).IsNull();
    }

    [Test]
    public async Task Lost_release_cannot_replay_over_a_committed_successor()
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync();
        ModerationEffectLeaseResult claim;
        await using (var claimant = database.Context())
            claim = await database.Repository(claimant, await database.AuthenticateAsync(claimant)).ClaimAsync(id, 1);
        ModerationEffectLeaseResult? successor = null;
        var fault = new LostAcknowledgement(database.Clock, true, async () =>
        {
            await using var recovery = database.Context();
            successor = await database.Repository(recovery, await database.AuthenticateAsync(recovery)).ClaimAsync(id, 3);
        });
        await using var context = database.Context(interceptors: [fault]);
        var repository = database.Repository(context, await database.AuthenticateAsync(context));
        var result = await repository.ReleaseAsync(id, claim.Effect!.LeaseId!.Value, 2);
        await Assert.That(fault.Injected).IsTrue();
        await Assert.That(successor!.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Known);
        await Assert.That(result.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Conflict);
        var current = await repository.ReadAsync(id);
        await Assert.That(current.Effect!.Revision).IsEqualTo(4);
        await Assert.That(current.Effect.LeaseId).IsEqualTo(successor.Effect!.LeaseId);
        await Assert.That(current.Effect.CompletedAt).IsNull();
    }

    [Test]
    public async Task Lost_commit_cannot_replay_over_a_committed_expired_lease_successor()
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync();
        ModerationEffectLeaseResult? successor = null;
        var fault = new LostAcknowledgement(database.Clock, true, async () =>
        {
            database.Clock.UtcNow = Now.AddMinutes(2);
            await using var recovery = database.Context();
            successor = await database.Repository(recovery, await database.AuthenticateAsync(recovery)).ClaimAsync(id, 2);
        });
        await using var context = database.Context(interceptors: [fault]);
        var repository = database.Repository(context, await database.AuthenticateAsync(context));
        var result = await repository.ClaimAsync(id, 1);
        await Assert.That(fault.Injected).IsTrue();
        await Assert.That(successor!.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Known);
        await Assert.That(result.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Conflict);
        var current = await repository.ReadAsync(id);
        await Assert.That(current.Effect!.LeaseId).IsEqualTo(successor.Effect!.LeaseId);
        await Assert.That(current.Effect.LeaseId == fault.CommittedLeaseId).IsFalse();
        await Assert.That(current.Effect.Revision).IsEqualTo(3);
        await Assert.That(current.Effect.LeaseExpiresAt).IsEqualTo((DateTimeOffset?)Now.AddMinutes(4));
    }

    [Test]
    public async Task Concurrent_native_claim_cannot_replace_the_first_committing_generation()
    {
        await using var database = await Database.CreateAsync();
        var id = await database.SeedAsync();
        var commit = new CommitGate();
        var starting = new TransactionStartingSignal();
        await using var firstContext = database.Context(interceptors: [commit]);
        await using var secondContext = database.Context(interceptors: [starting]);
        var first = database.Repository(firstContext, await database.AuthenticateAsync(firstContext));
        var second = database.Repository(secondContext, await database.AuthenticateAsync(secondContext));
        var firstClaim = Task.Run(() => first.ClaimAsync(id, 1));
        Task<ModerationEffectLeaseResult>? secondClaim = null;
        try
        {
            await commit.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            secondClaim = Task.Run(() => second.ClaimAsync(id, 1));
            await starting.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            commit.Release.TrySetResult();
        }
        var winner = await firstClaim.WaitAsync(TimeSpan.FromSeconds(30));
        var loser = await secondClaim!.WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(winner.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Known);
        await Assert.That(loser.Outcome is ModerationEffectLeaseOutcome.Conflict or ModerationEffectLeaseOutcome.Unavailable).IsTrue();
        var read = await first.ReadAsync(id);
        await Assert.That(read.Effect!.Revision).IsEqualTo(2);
        await Assert.That(read.Effect.LeaseId).IsEqualTo(winner.Effect!.LeaseId);
        await Assert.That(read.Effect.CompletedAt).IsNull();
    }

    private sealed class Database(string path) : IAsyncDisposable, ITenantContext
    {
        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid OtherTenantId { get; } = Guid.CreateVersion7();
        public Guid OwnerId { get; } = Guid.CreateVersion7();
        public string KeyId { get; } = ApiKeyHashing.CreateKeyId();
        public string Credential { get; private set; } = string.Empty;
        public MutableClock Clock { get; } = new();
        public ModerationNoticePrivacyFixture Privacy { get; private set; } = null!;
        public AuthorityScope Instance { get; } = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());

        public ExploreDbContext Context(Guid? tenantId = null, params IInterceptor[] interceptors)
        {
            var options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString())
                .UseSnakeCaseNamingConvention()
                .ReplaceService<IExecutionStrategyFactory, CommitRetryStrategyFactory>()
                .AddInterceptors([SqliteNamedLockTransactionInterceptor.Instance, .. interceptors]).Options;
            return new(options) { TenantContext = tenantId is { } selected ? new SelectedTenant(selected) : this };
        }

        public static async Task<Database> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"moderation-effect-lease-{Guid.CreateVersion7():N}.db");
            await LocalIdentitySqliteTemplate.CopyAsync(path, null, true, CancellationToken.None);
            var database = new Database(path);
            database.Privacy = await ModerationNoticePrivacyFixture.CreateAsync();
            var secret = ApiKeyHashing.CreateSecret();
            database.Credential = ApiKeyHashing.FormatPersistedApiKey(database.KeyId, secret);
            await using var context = database.Context();
            var status = await context.TenantStatuses.SingleAsync(row => row.IsActiveState);
            var service = new ServicePrincipal
            {
                Id = Guid.CreateVersion7(),
                Code = $"lease-{Guid.CreateVersion7():N}",
                DisplayName = "Maintenance"
            };
            var host = ModerationHostBinding.Establish(Guid.CreateVersion7(), database.Instance.TrustDomainId,
                database.Instance.Parent!.Id, database.Instance.Id, service.Id);
            context.AddRange(service, host);
            foreach (var id in new[] { database.TenantId, database.OtherTenantId })
            {
                var tenant = new Tenant
                {
                    Id = id,
                    Slug = $"lease-{id:N}",
                    FullName = "Lease tests",
                    TenantStatusId = status.Id,
                    TenantStatus = status
                };
                context.AddRange(tenant, ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), host, tenant));
            }
            context.Users.Add(new User
            {
                Id = database.OwnerId,
                Pii = new UserPii
                {
                    UserId = database.OwnerId,
                    Email = $"{database.OwnerId:N}@example.invalid",
                    FirstName = "Lease",
                    LastName = "Owner"
                }
            });
            context.ExternalApiKeys.Add(new ExternalApiKey
            {
                Id = Guid.CreateVersion7(),
                KeyId = database.KeyId,
                Name = "Lease maintenance",
                SecretHash = ApiKeyHashing.ComputeHash(secret),
                Scopes = "events:read",
                OwnerType = ExternalApiKeyOwnerType.InstanceAdmin,
                OwnerId = database.OwnerId,
                ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
                ExternalApiKeyStatus = null!,
                ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
                ExternalApiKeyCreditPeriod = null!
            });
            context.ModerationMachineSubjectBindings.Add(ModerationMachineSubjectBinding.Establish(
                Guid.CreateVersion7(), host.Id, ModerationMachineCredentialSource.Persisted, database.KeyId,
                null, ExternalApiKeyOwnerType.InstanceAdmin, database.OwnerId, service.Id));
            await context.SaveChangesAsync();
            return database;
        }

        public async Task<Guid> SeedAsync(
            bool advanceDecision = false, bool completed = false, string? capture = null, Guid? effectId = null,
            bool instanceScoped = false, DateTimeOffset? captureRetainUntil = null)
        {
            var scope = instanceScoped ? Instance : AuthorityScope.Tenant(Instance, TenantId);
            var subject = SubjectReference.Create(ModerationSubjectKind.Human, OwnerId, Instance);
            var created = Now.AddHours(-1);
            var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), created);
            var decision = ModerationDecision.Propose(Guid.CreateVersion7(), allegation.Id, subject, subject, Instance, scope,
                ModerationRuleReference.Create(scope.Id, "lease.rule", 1), [ModerationCapability.Publish],
                created, created.AddDays(1), null, 1, created);
            var operationId = Guid.CreateVersion7();
            var effect = ModerationEffectIntent.Create(effectId ?? Guid.CreateVersion7(), operationId, decision.Id, 1, null, null,
                Instance.TrustDomainId, (int)scope.Kind, scope.Id, (int)subject.Kind, subject.Id,
                (int)ModerationOperationKind.Propose, 1, 1, created, null, null);
            var receipt = ModerationOperationReceipt.Complete(operationId, new string('a', 64), Instance.TrustDomainId,
                (int)scope.Kind, scope.Id, (int)subject.Kind, subject.Id, OwnerId, new string('b', 64),
                decision.Id, null, 1, 1, 1, created);
            await using var context = Context();
            ModerationNoticeSource? source = null;
            if (capture is not null and not "missing")
            {
                var host = await context.ModerationHostBindings.AsNoTracking().SingleAsync();
                var epoch = await context.ModerationQualificationEpochs.AsNoTracking()
                    .SingleAsync(row => row.HostBindingId == host.Id && row.TenantId == TenantId);
                var original = new ModerationNoticeOriginalDecision(decision.Id, decision.Revision, decision.CaseId,
                    decision.State, ModerationNoticeScope.From(scope), subject.Kind, subject.Id,
                    ModerationNoticeScope.From(subject.Origin), new(decision.Rule.OwnerId, decision.Rule.Code, decision.Rule.Revision),
                    decision.CapabilityMask, decision.StartsAt, decision.EndsAt, decision.ReviewAt,
                    null, 0, null, null);
                var snapshot = new ModerationNoticeCaptureSnapshot(2, "event.moderation.capture.v2",
                    host.Id, host.SolutionId, host.InstanceId, TenantId, epoch.Id, 1, original, null, null, [], null);
                if (capture == "substantive")
                {
                    var findingId = Guid.CreateVersion7();
                    var stamp = new ModerationNoticeSourceStamp(Guid.CreateVersion7(), 1, "lease.fixture.source");
                    var text = new ModerationNoticeSafeText("Fixture-approved subject explanation.");
                    var target = new ModerationNoticeTarget(original.Scope, subject.Kind, subject.Id,
                        Guid.CreateVersion7(), 1, "publication.review", created.AddDays(1), "lease.fixture", stamp);
                    var interval = new ModerationReferenceInterval(ModerationReferenceTimingKind.Finite,
                        created, created.AddDays(1), null, null);
                    var modes = new ModerationConsequenceModes(
                        ModerationContentMode.PendingPublicationReview, ModerationManagementMode.CurrentAuthority,
                        ModerationResourceReadMode.CurrentAudience, ModerationNewAdmissionMode.CurrentEligibility,
                        ModerationExistingAdmissionMode.CurrentEligibility, ModerationPrivateDisclosureMode.CurrentAudience,
                        ModerationSettlementMode.NoBookingChange, ModerationCapacityMode.NoSeatChange,
                        ModerationCredentialMode.CurrentCredentialLifecycle, ModerationNotificationMode.DurableSafeSubjectAndAdmin,
                        ModerationRecognitionMode.NoCaptureFromThisControl, ModerationPendingWorkMode.CurrentPublicationApprovalRequired,
                        ModerationRestorationMode.RecomputeCurrentRightsOnly);
                    var notice = new ModerationNoticeSnapshot(1, effect.Id, effect.OperationId, effect.DecisionId,
                        effect.DecisionRevision, ModerationDecisionState.AwaitingApproval, null, null, null, null, null,
                        effect.OperationKind, original.Scope, subject.Kind, subject.Id, original.SubjectOrigin,
                        Guid.CreateVersion7(), 1, Guid.CreateVersion7(), 1, Guid.CreateVersion7(),
                        ModerationReferenceProfileId.CFG01, 1, stamp, Guid.CreateVersion7(), 1,
                        Guid.CreateVersion7(), original.Rule, original.Rule, text, text, [text],
                        text, text, text, text, [], [new(null, null, target, interval, modes)], created, created.AddYears(1), 6);
                    snapshot = snapshot with
                    {
                        Decision = original with { State = ModerationDecisionState.AwaitingApproval, FindingId = findingId },
                        Finding = new(findingId, 1, ModerationFindingState.Final, original.Rule),
                        Notice = notice,
                        Reviewers = [new(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Guid.CreateVersion7(),
                            Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Guid.CreateVersion7(), 1,
                            Guid.CreateVersion7(), created.AddDays(1))],
                        ValidUntil = created.AddDays(1),
                        ReviewerPolicySource = stamp
                    };
                    await Assert.That(snapshot.Matches(effect)).IsTrue();
                    await Assert.That(snapshot.HasNoSubstantiveWork).IsFalse();
                }
                snapshot = capture switch
                {
                    "version" => snapshot with { Version = 1 },
                    "tenant" => snapshot with { WriterTenantId = OtherTenantId },
                    "host" => snapshot with { HostInstanceId = Guid.CreateVersion7() },
                    "authority" => snapshot with { AuthoritySourceId = Guid.CreateVersion7() },
                    "qualification" => snapshot with { QualificationRevision = 2 },
                    "ancestry" => snapshot with
                    {
                        Decision = original with
                        { Scope = original.Scope with { Parent = ModerationNoticeScope.From(Instance.Parent!) } }
                    },
                    "subject-origin" => snapshot with
                    {
                        Decision = original with
                        { SubjectOrigin = ModerationNoticeScope.From(scope) }
                    },
                    _ => snapshot
                };
                source = ModerationNoticeSource.Capture(effect, capture == "source-tenant" ? null : TenantId,
                    capture == "malformed" ? "{" : JsonSerializer.Serialize(snapshot),
                    captureRetainUntil ?? snapshot.Notice?.RetainUntil);
            }
            if (advanceDecision) decision.RecordResponse(1, created.AddMinutes(1));
            if (completed)
            {
                var leaseId = Guid.CreateVersion7();
                effect.Claim(leaseId, 1, Now.AddMinutes(-1), Now.AddMinutes(1));
                effect.Complete(leaseId, 2, Now.AddSeconds(-30));
            }
            context.AddRange(allegation, decision, receipt, effect);
            if (source is not null) context.ModerationNoticeSources.Add(source);
            await context.SaveChangesAsync();
            if (capture is "hash" or "binding" or "bounds")
            {
                context.ModerationTrustDomainId = Instance.TrustDomainId;
                if (capture == "hash")
                    await context.ModerationNoticeSources.ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.SnapshotHash, new string('c', 64)));
                else if (capture == "binding")
                    await context.ModerationNoticeSources.ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.EffectBindingHash, new string('c', 64)));
                else
                    await context.ModerationNoticeSources.ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.SnapshotJson, new string(' ', 128 * 1024 + 1)));
            }
            return effect.Id;
        }

        public async Task<MachinePrincipalAccessor> AuthenticateAsync(ExploreDbContext context, Guid? tenantId = null)
        {
            var selected = tenantId ?? TenantId;
            var settings = new ModerationReconciliationOptions { Enabled = true, TenantId = selected };
            var credential = await new ModerationReconciliationCredentialProvider(
                new SettingsMonitor(settings),
                new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
                new SelectedSecret(this), Clock)
                .AuthenticateAsync(selected, CancellationToken.None);
            var machine = new MachinePrincipalAccessor(new HttpContextAccessor());
            machine.SetPrincipal(credential ?? throw new InvalidOperationException("Native fixture authentication failed."));
            return machine;
        }

        public ModerationEffectLeaseRepository Repository(ExploreDbContext context, MachinePrincipalAccessor machine)
            => new(context, machine, Bindings(context, machine),
                new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)), Clock);

        public ModerationMaintenanceAuthority MaintenanceAuthority(ExploreDbContext context, MachinePrincipalAccessor machine)
            => new(context, machine, Bindings(context, machine),
                new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)), Clock);

        private ModerationMachineSubjectBindingRepository Bindings(
            ExploreDbContext context, MachinePrincipalAccessor machine)
        {
            var admin = new AdminContext(new HttpContextAccessor(), new PlatformUserRoleRepository(context),
                new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
                new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
            var keys = new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context));
            return new(context, admin, machine, keys, new UnconfiguredKeys(), Clock);
        }

        public ServiceProvider DrainServices(
            Guid tenantId, List<DrainTenant> tenants, List<MachinePrincipalAccessor> machines,
            params IInterceptor[] interceptors)
        {
            var settings = new ModerationReconciliationOptions { Enabled = true, TenantId = tenantId };
            var services = new ServiceCollection();
            services.AddSingleton<IOptionsMonitor<ModerationReconciliationOptions>>(new SettingsMonitor(settings));
            services.AddScoped<ITenantContextAccessor>(_ =>
            {
                var tenant = new DrainTenant();
                tenants.Add(tenant);
                return tenant;
            });
            services.AddScoped<MachinePrincipalAccessor>(_ =>
            {
                var machine = new MachinePrincipalAccessor(new HttpContextAccessor());
                machines.Add(machine);
                return machine;
            });
            services.AddScoped<IMachinePrincipalExecutionAccessor>(provider =>
                provider.GetRequiredService<MachinePrincipalAccessor>());
            services.AddScoped<ExploreDbContext>(provider =>
                Context(provider.GetRequiredService<ITenantContextAccessor>().TenantId, interceptors));
            services.AddScoped<IExternalApiKeyAuthenticationRepository>(provider => new ExternalApiKeyAuthenticationRepository(
                new SecurityTestSupport.AdministrativeAuthorityTestFactory(provider.GetRequiredService<ExploreDbContext>())));
            services.AddScoped(provider => new ModerationReconciliationCredentialProvider(
                provider.GetRequiredService<IOptionsMonitor<ModerationReconciliationOptions>>(),
                provider.GetRequiredService<IExternalApiKeyAuthenticationRepository>(), new SelectedSecret(this), Clock));
            services.AddScoped<IModerationEffectLeaseRepository>(provider =>
                Repository(provider.GetRequiredService<ExploreDbContext>(), provider.GetRequiredService<MachinePrincipalAccessor>()));
            services.AddScoped<IModerationReconciliationQuery>(provider =>
                new ModerationReconciliationQuery(provider.GetRequiredService<ExploreDbContext>(),
                    provider.GetRequiredService<MachinePrincipalAccessor>(),
                    Bindings(provider.GetRequiredService<ExploreDbContext>(), provider.GetRequiredService<MachinePrincipalAccessor>())));
            services.AddScoped<ModerationEffectOutboxDispatcher>();
            services.AddScoped<IModerationEffectDispatcher>(provider =>
                provider.GetRequiredService<ModerationEffectOutboxDispatcher>());
            services.AddScoped(provider => new ModerationEffectRecoveryService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IModerationEffectDispatcher>(), Clock,
                NullLogger<ModerationEffectRecoveryService>.Instance));
            return services.BuildServiceProvider();
        }

        public async ValueTask DisposeAsync()
        {
            await Privacy.DisposeAsync();
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
        }
    }

    private sealed class DrainTenant : ITenantContextAccessor
    {
        public Guid? TenantId { get; private set; }
        public bool IsResolved => TenantId is not null;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
        public void Clear() => TenantId = null;
    }

    private sealed class SelectedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class OriginalPayloadReadCounter : DbCommandInterceptor
    {
        public int PayloadReads { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("moderation_notice_sources", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("snapshot_json", StringComparison.OrdinalIgnoreCase))
                PayloadReads++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class SettingsMonitor(ModerationReconciliationOptions settings) : IOptionsMonitor<ModerationReconciliationOptions>
    {
        public ModerationReconciliationOptions CurrentValue => settings;
        public ModerationReconciliationOptions Get(string? name) => settings;
        public IDisposable? OnChange(Action<ModerationReconciliationOptions, string?> listener) => null;
    }

    private sealed class SelectedSecret(Database database) : IModerationReconciliationSecretProvider
    {
        public ValueTask<string?> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<string?>(database.Credential);
        }
    }

    private sealed class UnconfiguredKeys : IConfiguredApiKeyResourceAuthorityReader
    {
        public ValueTask<IConfiguredApiKeyResourceAuthorityLease?> AcquireAuthenticatedAsync(
            string keyId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IConfiguredApiKeyResourceAuthorityLease?>(null);

        public ValueTask<IConfiguredApiKeyResourceAuthorityLease?> AcquireAsync(
            string keyId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IConfiguredApiKeyResourceAuthorityLease?>(null);

        public ValueTask<ApiKeyPrincipalContext?> ReadAsync(string keyId, DateTimeOffset evaluationUtc, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ApiKeyPrincipalContext?>(null);
    }

    private sealed class RejectInertCompletion(bool afterFlush) : SaveChangesInterceptor
    {
        public bool Injected { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!afterFlush && !Injected
                && eventData.Context!.ChangeTracker.Entries<ModerationEffectIntent>()
                    .Any(entry => entry.Entity.CompletedAt is not null))
            {
                Injected = true;
                throw new InvalidOperationException("Native inert completion rejected before flush.");
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (afterFlush && !Injected
                && eventData.Context!.ChangeTracker.Entries<ModerationEffectIntent>()
                    .Any(entry => entry.Entity.CompletedAt is not null))
            {
                Injected = true;
                throw new InvalidOperationException("Native inert completion rejected after flush.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class EnlistInertCompletionDisclosure(Guid tenantId, InertDisclosureAdvancement advancement)
        : SaveChangesInterceptor
    {
        public bool ObservedCompletion { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!ObservedCompletion && eventData.Context is ExploreDbContext context
                && !context.ChangeTracker.HasChanges()
                && context.ChangeTracker.Entries<ModerationEffectIntent>().Any(entry => entry.Entity.CompletedAt is not null))
            {
                ObservedCompletion = true;
                context.DisclosureMutations.Enlist([tenantId]);
                advancement.Armed = true;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class InertDisclosureAdvancement(
        Guid tenantId, long? originalEpoch, Func<ExploreDbContext, Task> afterAdvancement) : DbCommandInterceptor
    {
        private bool observing;
        public bool Armed { get; set; }
        public bool ObservedAdvancement { get; private set; }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!Armed || observing || ObservedAdvancement || eventData.Context is not ExploreDbContext context)
                return result;
            observing = true;
            try
            {
                var epoch = await context.Set<EventDiscoveryRevision>().AsNoTracking()
                    .Where(row => row.TenantId == tenantId).Select(row => (long?)row.DisclosureEpoch)
                    .SingleOrDefaultAsync(cancellationToken);
                if (epoch > originalEpoch.GetValueOrDefault())
                {
                    ObservedAdvancement = true;
                    await afterAdvancement(context);
                }
            }
            finally { observing = false; }
            return result;
        }
    }

    private sealed class ExpireClaimOnSave(MutableClock clock) : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Observed = true;
            clock.UtcNow = Now + TimeSpan.FromMinutes(2);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CommitGate : DbTransactionInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }

    private sealed class TransactionStartingSignal : DbTransactionInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class LostCommitException(bool verify) : TimeoutException("Native SQLite commit acknowledgement lost.")
    {
        public bool Verify { get; } = verify;
    }

    private sealed class LostCompletionBeforeCommit(bool verify, Func<Task> recover) : DbTransactionInterceptor
    {
        public bool Injected { get; private set; }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (!Injected)
            {
                Injected = true;
                // The staged completion never commits. A real native successor takes the expired
                // lease after rollback, before EF verifies or replays the ambiguous invocation.
                await eventData.Context!.Database.CurrentTransaction!.RollbackAsync(cancellationToken);
                await recover();
                throw new LostCommitException(verify);
            }
            return result;
        }
    }

    private sealed class LostAcknowledgement(MutableClock clock, bool verify, Func<Task>? afterCommit = null) : DbTransactionInterceptor
    {
        public bool Injected { get; private set; }
        public Guid? CommittedLeaseId { get; private set; }
        public override async Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!Injected)
            {
                Injected = true;
                CommittedLeaseId = eventData.Context!.ChangeTracker.Entries<ModerationEffectIntent>().Single().Entity.LeaseId;
                clock.UtcNow = clock.UtcNow.AddSeconds(30);
                if (afterCommit is not null) await afterCommit();
                throw new LostCommitException(verify);
            }
        }
    }

    // SQLite normally has no commit retry strategy. This fault-only strategy exercises EF's real
    // transaction/reverification path without replacing any repository, authority or database behavior.
    private sealed class CommitRetryStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new CommitRetryStrategy(dependencies);
    }

    private sealed class CommitRetryStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 2, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is LostCommitException;
        protected override bool ShouldVerifySuccessOn(Exception exception) => exception is LostCommitException { Verify: true };
    }
}
