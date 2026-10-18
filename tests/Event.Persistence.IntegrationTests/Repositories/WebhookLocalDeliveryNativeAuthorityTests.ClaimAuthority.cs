using System.Data.Common;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class WebhookLocalDeliveryNativeAuthorityTests
{
    [Test]
    [Arguments("succeeded", WebhookLocalDeliveryStatus.Succeeded, WebhookDeliveryAttemptOutcome.Succeeded)]
    [Arguments("retry", WebhookLocalDeliveryStatus.RetryDue, WebhookDeliveryAttemptOutcome.Failed)]
    [Arguments("dead-letter", WebhookLocalDeliveryStatus.DeadLettered, WebhookDeliveryAttemptOutcome.Failed)]
    [Arguments("abandoned", WebhookLocalDeliveryStatus.Abandoned, WebhookDeliveryAttemptOutcome.Abandoned)]
    public async Task ClaimAuthorityCommitsOnlyOriginalSettlementEvidence(
        string settlement, WebhookLocalDeliveryStatus expectedStatus, WebhookDeliveryAttemptOutcome expectedOutcome)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, instanceFirst: true);
        Guid lease = ClaimAuthoritySeedLease(rows.First);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);

        await new EfCoreUnitOfWork(runtime).ExecuteInTransactionAsync(async token =>
        {
            var target = await ClaimAuthorityReadAsync(runtime, rows.First, lease, token);
            var endpoints = new WebhookEndpointRepository(runtime);
            if (settlement == "succeeded")
            {
                await Assert.That(await endpoints.MarkSuccessAsync(rows.First.TenantId, rows.First.Endpoint.Id,
                    target.Id, lease, 1, Now.UtcDateTime, token)).IsTrue();
                target.MarkSucceeded(lease, 1, Now);
            }
            else
            {
                await Assert.That(await endpoints.RecordFailureAsync(rows.First.TenantId, rows.First.Endpoint.Id,
                    target.Id, lease, 1, Now.UtcDateTime, "http_non_success", 5, token)).IsNotNull();
                if (settlement == "retry") target.ScheduleRetry(lease, 1, Now.AddMinutes(1), Now);
                else if (settlement == "dead-letter") target.DeadLetter(lease, 1, Now);
                else target.Abandon(lease, 1, Now);
            }
            var attempt = ClaimAuthorityEvidence(target, expectedOutcome);
            if (settlement != "succeeded") attempt.FailureCategory = "http_non_success";
            if (settlement == "retry") attempt.NextRetryAt = Now.AddMinutes(1).UtcDateTime;
            await new WebhookDeliveryAttemptRepository(runtime).CreateAsync(attempt, token);
            await Assert.That(runtime.ChangeTracker.Entries<WebhookEndpoint>().Any()).IsFalse();
        });

        await using var verify = fixture.CreateDbContext();
        var targetState = await verify.WebhookLocalTargetSnapshots.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(target => target.Id == rows.First.Target.Id);
        var evidence = await verify.WebhookDeliveryAttempts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(attempt => attempt.MessageId == rows.First.Message.Id);
        await Assert.That(targetState.DeliveryStatus).IsEqualTo(expectedStatus);
        await Assert.That(targetState.ProcessingLeaseToken).IsNull();
        await Assert.That(targetState.DeliveryFence).IsEqualTo(1);
        await Assert.That(evidence.Outcome).IsEqualTo(expectedOutcome);
        await Assert.That(evidence.EndpointId).IsEqualTo(rows.First.Endpoint.Id);
        await Assert.That(evidence.TenantId).IsEqualTo(rows.First.TenantId);
        await Assert.That(evidence.AttemptNumber).IsEqualTo(1);
        await Assert.That(evidence.ProcessingFence).IsEqualTo(1);
        await Assert.That(evidence.ProcessingLeaseToken).IsNull();
        await Assert.That(await verify.WebhookEndpoints.IgnoreQueryFilters()
            .Where(endpoint => endpoint.Id == rows.First.Endpoint.Id)
            .Select(endpoint => endpoint.DeliveryStateVersion).SingleAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments("endpoint")]
    [Arguments("message")]
    [Arguments("tenant")]
    [Arguments("outcome")]
    [Arguments("fence")]
    [Arguments("attempt-number")]
    public async Task ClaimAuthorityRejectsSubstitutedEvidenceAndRollsBackHealth(string substitution)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, instanceFirst: true);
        Guid lease = ClaimAuthoritySeedLease(rows.First);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);

        await Assert.That(async () => await new EfCoreUnitOfWork(runtime).ExecuteInTransactionAsync(async token =>
        {
            var target = await ClaimAuthorityReadAsync(runtime, rows.First, lease, token);
            await ClaimAuthorityMarkSuccessAsync(runtime, rows.First, lease, token);
            target.MarkSucceeded(lease, 1, Now);
            var attempt = ClaimAuthorityEvidence(target, WebhookDeliveryAttemptOutcome.Succeeded);
            // Future is a different existing graph in the SAME tenant. Ordinary tenant visibility
            // alone must not let this original claim authorize its endpoint or message.
            switch (substitution)
            {
                case "endpoint": attempt.EndpointId = rows.Future.Endpoint.Id; break;
                case "message": attempt.MessageId = rows.Future.Message.Id; break;
                case "tenant": attempt.TenantId = rows.Second.TenantId; break;
                case "outcome": attempt.Outcome = WebhookDeliveryAttemptOutcome.Failed; break;
                case "fence": attempt.ProcessingFence = 2; break;
                case "attempt-number": attempt.AttemptNumber = 2; break;
                default: throw new ArgumentOutOfRangeException(nameof(substitution));
            }
            await new WebhookDeliveryAttemptRepository(runtime).CreateAsync(attempt, token);
        })).Throws<InvalidOperationException>()
            .And.Satisfies(exception => exception != null && exception.Message == "tracked_tenant_authority_mismatch");
        await AssertClaimAuthorityRolledBackAsync(rows.First, lease);
    }

    [Test]
    [Arguments("transactionless")]
    [Arguments("replaced-transaction")]
    [Arguments("reused-context")]
    public async Task ClaimAuthorityCannotOutliveItsOriginalTransactionAndContextLease(string ownerChange)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, instanceFirst: true);
        Guid lease = ClaimAuthoritySeedLease(rows.First);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
        try
        {
            if (ownerChange == "transactionless")
            {
                await ClaimAuthorityReadAsync(runtime, rows.First, lease, CancellationToken.None);
            }
            else
            {
                await runtime.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var original = await runtime.Database.BeginTransactionAsync();
                    await ClaimAuthorityReadAsync(runtime, rows.First, lease, CancellationToken.None);
                    await original.CommitAsync();
                });
            }
            if (ownerChange == "reused-context")
            {
                var originalContext = runtime;
                await runtime.DisposeAsync();
                runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
                await Assert.That(ReferenceEquals(originalContext, runtime)).IsTrue();
            }

            await Assert.That(async () => await new EfCoreUnitOfWork(runtime).ExecuteInTransactionAsync(async token =>
            {
                // An ordinary tenant query deliberately does not re-admit the original claim.
                var target = await runtime.WebhookLocalTargetSnapshots
                    .SingleAsync(value => value.Id == rows.First.Target.Id, token);
                await ClaimAuthorityMarkSuccessAsync(runtime, rows.First, lease, token);
                target.MarkSucceeded(lease, 1, Now);
                await new WebhookDeliveryAttemptRepository(runtime).CreateAsync(
                    ClaimAuthorityEvidence(target, WebhookDeliveryAttemptOutcome.Succeeded), token);
            })).Throws<InvalidOperationException>()
                .And.Satisfies(exception => exception != null && exception.Message == "tracked_tenant_authority_mismatch");
        }
        finally
        {
            await runtime.DisposeAsync();
        }
        await AssertClaimAuthorityRolledBackAsync(rows.First, lease);
    }

    [Test]
    public async Task ClaimAuthorityRejectsPretrackedEndpointSpoofDespiteRewrittenOriginalValues()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, instanceFirst: true);
        Guid lease = ClaimAuthoritySeedLease(rows.First);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);

        await Assert.That(async () => await new EfCoreUnitOfWork(runtime).ExecuteInTransactionAsync(async token =>
        {
            var pretracked = await runtime.WebhookLocalTargetSnapshots
                .SingleAsync(target => target.Id == rows.First.Target.Id, token);
            var endpoint = runtime.Entry(pretracked).Property(target => target.WebhookEndpointId);
            endpoint.CurrentValue = rows.Future.Endpoint.Id;
            endpoint.OriginalValue = rows.Future.Endpoint.Id;
            endpoint.IsModified = false;
            var target = await ClaimAuthorityReadAsync(runtime, rows.First, lease, token);
            await ClaimAuthorityMarkSuccessAsync(runtime, rows.First, lease, token);
            target.MarkSucceeded(lease, 1, Now);
            await new WebhookDeliveryAttemptRepository(runtime).CreateAsync(
                ClaimAuthorityEvidence(target, WebhookDeliveryAttemptOutcome.Succeeded), token);
        })).Throws<InvalidOperationException>()
            .And.Satisfies(exception => exception != null && exception.Message == "tracked_tenant_authority_mismatch");
        await AssertClaimAuthorityRolledBackAsync(rows.First, lease);
    }

    [Test]
    public async Task ClaimAuthorityCannotBindEvidenceAfterTerminalTargetWasFlushed()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, instanceFirst: true);
        Guid lease = ClaimAuthoritySeedLease(rows.First);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);

        await Assert.That(async () => await new EfCoreUnitOfWork(runtime).ExecuteInTransactionAsync(async token =>
        {
            var target = await ClaimAuthorityReadAsync(runtime, rows.First, lease, token);
            await ClaimAuthorityMarkSuccessAsync(runtime, rows.First, lease, token);
            // Persist the terminal row before evidence without depending on the guard under test
            // to authorize this setup. Runtime already has this bounded target UPDATE permission.
            await Assert.That(await runtime.WebhookLocalTargetSnapshots
                .Where(value => value.Id == target.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.DeliveryStatusId, (int)WebhookLocalDeliveryStatus.Succeeded)
                    .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
                    .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
                    .SetProperty(value => value.ProcessingLeaseExpiresAtUtc, (DateTimeOffset?)null)
                    .SetProperty(value => value.ConcurrencyVersion, value => value.ConcurrencyVersion + 1)
                    .SetProperty(value => value.NextActionAtUtc, Now), token)).IsEqualTo(1);
            await runtime.Entry(target).ReloadAsync(token);
            await Assert.That(target.DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.Succeeded);
            await Assert.That(await new WebhookEndpointRepository(runtime).MarkSuccessAsync(
                rows.First.TenantId, rows.First.Endpoint.Id, target.Id, lease, 1, Now.UtcDateTime, token)).IsFalse();
            await new WebhookDeliveryAttemptRepository(runtime).CreateAsync(
                ClaimAuthorityEvidence(target, WebhookDeliveryAttemptOutcome.Succeeded), token);
        })).Throws<InvalidOperationException>()
            .And.Satisfies(exception => exception != null && exception.Message == "tracked_tenant_authority_mismatch");
        await AssertClaimAuthorityRolledBackAsync(rows.First, lease);
    }

    [Test]
    public async Task ClaimAuthorityKeepsTwoOriginalClaimsIndependentInOneTransaction()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, instanceFirst: true);
        var other = AddGraph(seed, rows.First.TenantId, Now.AddMinutes(-3),
            instanceId: rows.First.Endpoint.InstanceId);
        Guid firstLease = ClaimAuthoritySeedLease(rows.First);
        Guid otherLease = ClaimAuthoritySeedLease(other);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);

        await new EfCoreUnitOfWork(runtime).ExecuteInTransactionAsync(async token =>
        {
            var first = await ClaimAuthorityReadAsync(runtime, rows.First, firstLease, token);
            var second = await ClaimAuthorityReadAsync(runtime, other, otherLease, token);
            await ClaimAuthorityMarkSuccessAsync(runtime, rows.First, firstLease, token);
            await ClaimAuthorityMarkSuccessAsync(runtime, other, otherLease, token);
            first.MarkSucceeded(firstLease, 1, Now);
            second.MarkSucceeded(otherLease, 1, Now);
            await new WebhookDeliveryAttemptRepository(runtime).CreateManyAsync(
                [ClaimAuthorityEvidence(first, WebhookDeliveryAttemptOutcome.Succeeded),
                    ClaimAuthorityEvidence(second, WebhookDeliveryAttemptOutcome.Succeeded)], token);
        });

        await using var verify = fixture.CreateDbContext();
        var attempts = await verify.WebhookDeliveryAttempts.IgnoreQueryFilters().AsNoTracking()
            .Where(attempt => attempt.TenantId == rows.First.TenantId).ToListAsync();
        await Assert.That(attempts.Count).IsEqualTo(2);
        await Assert.That(attempts.Select(attempt => attempt.MessageId))
            .IsEquivalentTo([rows.First.Message.Id, other.Message.Id]);
        await Assert.That(attempts.All(attempt => attempt.Outcome == WebhookDeliveryAttemptOutcome.Succeeded)).IsTrue();
        await Assert.That(await verify.WebhookLocalTargetSnapshots.IgnoreQueryFilters()
            .CountAsync(target => (target.Id == rows.First.Target.Id || target.Id == other.Target.Id)
                && target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.Succeeded
                && target.ProcessingLeaseToken == null)).IsEqualTo(2);
        await Assert.That(await verify.WebhookEndpoints.IgnoreQueryFilters()
            .CountAsync(endpoint => (endpoint.Id == rows.First.Endpoint.Id || endpoint.Id == other.Endpoint.Id)
                && endpoint.DeliveryStateVersion == 1)).IsEqualTo(2);
    }

    [Test]
    public async Task ClaimAuthorityRejectsASecondEvidenceObjectForTheSameClaim()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, instanceFirst: true);
        Guid lease = ClaimAuthoritySeedLease(rows.First);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);

        await Assert.That(async () => await new EfCoreUnitOfWork(runtime).ExecuteInTransactionAsync(async token =>
        {
            var target = await ClaimAuthorityReadAsync(runtime, rows.First, lease, token);
            await ClaimAuthorityMarkSuccessAsync(runtime, rows.First, lease, token);
            target.MarkSucceeded(lease, 1, Now);
            await new WebhookDeliveryAttemptRepository(runtime).CreateManyAsync(
                [ClaimAuthorityEvidence(target, WebhookDeliveryAttemptOutcome.Succeeded),
                    ClaimAuthorityEvidence(target, WebhookDeliveryAttemptOutcome.Succeeded)], token);
        })).Throws<InvalidOperationException>()
            .And.Satisfies(exception => exception != null && exception.Message == "tracked_tenant_authority_mismatch");
        await AssertClaimAuthorityRolledBackAsync(rows.First, lease);
    }

    [Test]
    public async Task ClaimAuthorityRejectsSameVersionGraphReplacementAfterAdmissionSavepointRollback()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, instanceFirst: true);
        Guid lease = ClaimAuthoritySeedLease(rows.First);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Assert.That(async () => await new EfCoreUnitOfWork(runtime).ExecuteInTransactionAsync(async token =>
        {
            var transaction = runtime.Database.CurrentTransaction!;
            await transaction.CreateSavepointAsync("before_claim_admission", token);
            var target = await ClaimAuthorityReadAsync(runtime, rows.First, lease, token);
            await ClaimAuthorityMarkSuccessAsync(runtime, rows.First, lease, token);
            target.MarkSucceeded(lease, 1, Now);
            await transaction.RollbackToSavepointAsync("before_claim_admission", token);

            // The original transaction object and tracked snapshot survive, but its row lock
            // does not. A separate real Runtime session changes the graph without a version bump.
            await using (var writer = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId, token))
            {
                await Assert.That(await writer.WebhookLocalTargetSnapshots
                    .Where(value => value.Id == rows.First.Target.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(value => value.WebhookEndpointId, rows.Future.Endpoint.Id), token)).IsEqualTo(1);
                await Assert.That(await writer.WebhookLocalTargetSnapshots.AsNoTracking()
                    .Where(value => value.Id == rows.First.Target.Id)
                    .Select(value => value.ConcurrencyVersion).SingleAsync(token))
                    .IsEqualTo(rows.First.Target.ConcurrencyVersion);
            }

            await new WebhookDeliveryAttemptRepository(runtime).CreateAsync(
                ClaimAuthorityEvidence(target, WebhookDeliveryAttemptOutcome.Succeeded), token);
        }, deadline.Token)).Throws<InvalidOperationException>()
            .And.Satisfies(exception => exception != null && exception.Message == "tracked_tenant_authority_mismatch");

        // Rollback must preserve the independent writer's committed graph, not overwrite it
        // with the original transaction's stale terminal target or evidence.
        await AssertClaimAuthorityRolledBackAsync(rows.First, lease, rows.Future.Endpoint.Id);
        await using var verify = fixture.CreateDbContext();
        await Assert.That(await verify.WebhookEndpoints.IgnoreQueryFilters()
            .Where(endpoint => endpoint.Id == rows.Future.Endpoint.Id)
            .Select(endpoint => endpoint.DeliveryStateVersion).SingleAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task ClaimAuthorityCaughtBindingRejectionStillRollsBackEarlierHealth()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, instanceFirst: true);
        Guid lease = ClaimAuthoritySeedLease(rows.First);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);

        await Assert.That(async () => await new EfCoreUnitOfWork(runtime).ExecuteInTransactionAsync(async token =>
        {
            var target = await ClaimAuthorityReadAsync(runtime, rows.First, lease, token);
            await ClaimAuthorityMarkSuccessAsync(runtime, rows.First, lease, token);
            target.MarkSucceeded(lease, 1, Now);
            var attempt = ClaimAuthorityEvidence(target, WebhookDeliveryAttemptOutcome.Succeeded);
            attempt.EndpointId = rows.Future.Endpoint.Id;
            await Assert.That(async () => await new WebhookDeliveryAttemptRepository(runtime)
                .CreateAsync(attempt, token)).Throws<InvalidOperationException>()
                .And.Satisfies(exception => exception != null && exception.Message == "tracked_tenant_authority_mismatch");
            // Deliberately catch the rejection and remove pending changes. The surrounding UoW
            // must still refuse to commit the endpoint mutation that preceded the failed binding.
            runtime.ChangeTracker.Clear();
        })).Throws<ConcurrencyConflictException>();
        await AssertClaimAuthorityRolledBackAsync(rows.First, lease);
    }

    [Test]
    public async Task ClaimAuthorityCaughtEvidenceInsertFailureStillRollsBackEarlierHealth()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, instanceFirst: true);
        var prior = AddGraph(seed, rows.First.TenantId, Now.AddMinutes(-3),
            instanceId: rows.First.Endpoint.InstanceId);
        Guid priorLease = ClaimAuthoritySeedLease(prior);
        prior.Target.MarkSucceeded(priorLease, 1, Now);
        var existingEvidence = ClaimAuthorityEvidence(prior.Target, WebhookDeliveryAttemptOutcome.Succeeded);
        seed.WebhookDeliveryAttempts.Add(existingEvidence);
        Guid lease = ClaimAuthoritySeedLease(rows.First);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);

        await Assert.That(async () => await new EfCoreUnitOfWork(runtime).ExecuteInTransactionAsync(async token =>
        {
            var target = await ClaimAuthorityReadAsync(runtime, rows.First, lease, token);
            await ClaimAuthorityMarkSuccessAsync(runtime, rows.First, lease, token);
            target.MarkSucceeded(lease, 1, Now);
            var attempt = ClaimAuthorityEvidence(target, WebhookDeliveryAttemptOutcome.Succeeded);
            attempt.Id = existingEvidence.Id;
            // The other evidence is not tracked in Runtime: binding is valid, and PostgreSQL
            // must reject this insert. EF's savepoint rollback does not undo earlier native health.
            await Assert.That(async () => await new WebhookDeliveryAttemptRepository(runtime)
                .CreateAsync(attempt, token)).Throws<DbUpdateException>()
                .And.Satisfies(exception => exception != null
                    && exception.InnerException is PostgresException { SqlState: "23505" });
            runtime.ChangeTracker.Clear();
        })).Throws<ConcurrencyConflictException>();

        await using var verify = fixture.CreateDbContext();
        var persistedTarget = await verify.WebhookLocalTargetSnapshots.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(target => target.Id == rows.First.Target.Id);
        var endpoint = await verify.WebhookEndpoints.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == rows.First.Endpoint.Id);
        await Assert.That(persistedTarget.DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.Delivering);
        await Assert.That(persistedTarget.ProcessingLeaseToken).IsEqualTo(lease);
        await Assert.That(persistedTarget.DeliveryFence).IsEqualTo(1);
        await Assert.That(persistedTarget.ConcurrencyVersion).IsEqualTo(rows.First.Target.ConcurrencyVersion);
        await Assert.That(endpoint.DeliveryStateVersion).IsEqualTo(0);
        await Assert.That(endpoint.LastSuccessAt).IsNull();
        await Assert.That(await verify.WebhookDeliveryAttempts.IgnoreQueryFilters()
            .CountAsync(attempt => attempt.MessageId == rows.First.Message.Id)).IsEqualTo(0);
        var preserved = await verify.WebhookDeliveryAttempts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(attempt => attempt.Id == existingEvidence.Id);
        await Assert.That(preserved.TenantId).IsEqualTo(prior.TenantId);
        await Assert.That(preserved.MessageId).IsEqualTo(prior.Message.Id);
        await Assert.That(preserved.EndpointId).IsEqualTo(prior.Endpoint.Id);
        await Assert.That(preserved.Outcome).IsEqualTo(WebhookDeliveryAttemptOutcome.Succeeded);
    }

    [Test]
    public async Task ClaimAuthorityMutableBatchCannotHideCaughtInsertFailure()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, instanceFirst: true);
        var prior = AddGraph(seed, rows.First.TenantId, Now.AddMinutes(-3),
            instanceId: rows.First.Endpoint.InstanceId);
        Guid priorLease = ClaimAuthoritySeedLease(prior);
        prior.Target.MarkSucceeded(priorLease, 1, Now);
        var existingEvidence = ClaimAuthorityEvidence(prior.Target, WebhookDeliveryAttemptOutcome.Succeeded);
        seed.WebhookDeliveryAttempts.Add(existingEvidence);
        Guid lease = ClaimAuthoritySeedLease(rows.First);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        var attemptEntity = seed.Model.FindEntityType(typeof(WebhookDeliveryAttempt))!;
        var barrier = new ClaimEvidenceInsertBarrier(seed.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(attemptEntity.GetTableName()!, attemptEntity.GetSchema()));
        await using var runtime = fixture.CreateEnrolledTenantDbContext(
            new ClaimEvidenceTenantScope(rows.First.TenantId), barrier);

        await Assert.That(async () => await new EfCoreUnitOfWork(runtime).ExecuteInTransactionAsync(async token =>
        {
            var target = await ClaimAuthorityReadAsync(runtime, rows.First, lease, token);
            await ClaimAuthorityMarkSuccessAsync(runtime, rows.First, lease, token);
            target.MarkSucceeded(lease, 1, Now);
            var attempt = ClaimAuthorityEvidence(target, WebhookDeliveryAttemptOutcome.Succeeded);
            attempt.Id = existingEvidence.Id;
            var callerAttempts = new List<WebhookDeliveryAttempt> { attempt };
            var inserting = new WebhookDeliveryAttemptRepository(runtime).CreateManyAsync(callerAttempts, token);
            try
            {
                await barrier.Reached.WaitAsync(TimeSpan.FromSeconds(30), token);
                // EF already owns the admitted INSERT. Mutating the caller's collection must
                // not remove that exact evidence object from the repository's failure custody.
                callerAttempts.Clear();
            }
            finally
            {
                barrier.Release();
            }
            await Assert.That(async () => await inserting).Throws<DbUpdateException>()
                .And.Satisfies(exception => exception != null
                    && exception.InnerException is PostgresException { SqlState: "23505" });
            runtime.ChangeTracker.Clear();
        })).Throws<ConcurrencyConflictException>();

        await using var verify = fixture.CreateDbContext();
        var persistedTarget = await verify.WebhookLocalTargetSnapshots.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(target => target.Id == rows.First.Target.Id);
        var endpoint = await verify.WebhookEndpoints.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == rows.First.Endpoint.Id);
        await Assert.That(persistedTarget.DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.Delivering);
        await Assert.That(persistedTarget.ProcessingLeaseToken).IsEqualTo(lease);
        await Assert.That(persistedTarget.DeliveryFence).IsEqualTo(1);
        await Assert.That(persistedTarget.ConcurrencyVersion).IsEqualTo(rows.First.Target.ConcurrencyVersion);
        await Assert.That(endpoint.DeliveryStateVersion).IsEqualTo(0);
        await Assert.That(endpoint.LastSuccessAt).IsNull();
        await Assert.That(await verify.WebhookDeliveryAttempts.IgnoreQueryFilters()
            .CountAsync(attempt => attempt.MessageId == rows.First.Message.Id)).IsEqualTo(0);
        var preserved = await verify.WebhookDeliveryAttempts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(attempt => attempt.Id == existingEvidence.Id);
        await Assert.That(preserved.TenantId).IsEqualTo(prior.TenantId);
        await Assert.That(preserved.MessageId).IsEqualTo(prior.Message.Id);
        await Assert.That(preserved.EndpointId).IsEqualTo(prior.Endpoint.Id);
        await Assert.That(preserved.Outcome).IsEqualTo(WebhookDeliveryAttemptOutcome.Succeeded);
    }

    private sealed record ClaimEvidenceTenantScope(Guid TenantId) : ITenantContext;

    private sealed class ClaimEvidenceInsertBarrier(string attemptsTable) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int entered;

        internal Task Reached => reached.Task;
        internal void Release() => released.TrySetResult();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await PauseInsertAsync(command, eventData, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await PauseInsertAsync(command, eventData, cancellationToken);
            return result;
        }

        private async Task PauseInsertAsync(
            DbCommand command, CommandEventData eventData, CancellationToken cancellationToken)
        {
            if (eventData.CommandSource != CommandSource.SaveChanges
                || !command.CommandText.Contains($"INSERT INTO {attemptsTable}", StringComparison.Ordinal)
                || Interlocked.Exchange(ref entered, 1) != 0)
                return;
            reached.TrySetResult();
            await released.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }

    [Test]
    public async Task ClaimAuthorityRejectsHiddenConsumedEvidenceMutationDuringUnrelatedSave()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, instanceFirst: true);
        Guid lease = ClaimAuthoritySeedLease(rows.First);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);

        await Assert.That(async () => await new EfCoreUnitOfWork(runtime).ExecuteInTransactionAsync(async token =>
        {
            var target = await ClaimAuthorityReadAsync(runtime, rows.First, lease, token);
            await ClaimAuthorityMarkSuccessAsync(runtime, rows.First, lease, token);
            target.MarkSucceeded(lease, 1, Now);
            var evidence = await new WebhookDeliveryAttemptRepository(runtime).CreateAsync(
                ClaimAuthorityEvidence(target, WebhookDeliveryAttemptOutcome.Succeeded), token);

            // A successful first save consumes FK allowance, not immutable object identity.
            // Rewrite EF metadata to hide the changed evidence from ordinary modified-entry/FK checks.
            evidence.EndpointId = rows.Future.Endpoint.Id;
            evidence.Outcome = WebhookDeliveryAttemptOutcome.Failed;
            var evidenceEntry = runtime.Entry(evidence);
            evidenceEntry.Property(value => value.EndpointId).OriginalValue = evidence.EndpointId;
            evidenceEntry.Property(value => value.OutcomeId).OriginalValue = evidence.OutcomeId;
            evidenceEntry.State = EntityState.Unchanged;
            var unrelated = await runtime.WebhookLocalTargetSnapshots
                .SingleAsync(value => value.Id == rows.Future.Target.Id, token);
            unrelated.UpdatedAt = Now.AddMinutes(1).UtcDateTime;
            await Assert.That(async () => await runtime.SaveChangesAsync(token)).Throws<InvalidOperationException>()
                .And.Satisfies(exception => exception != null && exception.Message == "tracked_tenant_authority_mismatch");
            runtime.ChangeTracker.Clear();
        })).Throws<ConcurrencyConflictException>();

        await AssertClaimAuthorityRolledBackAsync(rows.First, lease);
        await using var verify = fixture.CreateDbContext();
        await Assert.That(await verify.WebhookLocalTargetSnapshots.IgnoreQueryFilters()
            .Where(target => target.Id == rows.Future.Target.Id).Select(target => target.UpdatedAt).SingleAsync())
            .IsEqualTo(rows.Future.Target.UpdatedAt);
    }

    private static Guid ClaimAuthoritySeedLease(TargetGraph graph)
    {
        Guid lease = Guid.CreateVersion7();
        graph.Target.ClaimForDelivery("claim-authority", lease, Now.AddMinutes(2), Now);
        return lease;
    }

    private static async Task<WebhookLocalTargetSnapshot> ClaimAuthorityReadAsync(
        ExploreDbContext runtime, TargetGraph graph, Guid lease, CancellationToken cancellationToken)
    {
        var target = await new WebhookLocalTargetRepository(runtime).GetActiveClaimAsync(
            graph.TenantId, graph.Target.Id, lease, 1, Now, cancellationToken);
        await Assert.That(target).IsNotNull();
        return target!;
    }

    private static async Task ClaimAuthorityMarkSuccessAsync(
        ExploreDbContext runtime, TargetGraph graph, Guid lease, CancellationToken cancellationToken) =>
        await Assert.That(await new WebhookEndpointRepository(runtime).MarkSuccessAsync(
            graph.TenantId, graph.Endpoint.Id, graph.Target.Id, lease, 1, Now.UtcDateTime, cancellationToken)).IsTrue();

    private static WebhookDeliveryAttempt ClaimAuthorityEvidence(
        WebhookLocalTargetSnapshot target, WebhookDeliveryAttemptOutcome outcome) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = target.TenantId,
        MessageId = target.WebhookMessageId,
        EndpointId = target.WebhookEndpointId,
        AttemptNumber = 1,
        ProcessingFence = 1,
        Outcome = outcome,
        ScheduledAt = Now.UtcDateTime,
        SentAt = Now.UtcDateTime,
        CompletedAt = Now.UtcDateTime,
        CreatedAt = Now.UtcDateTime
    };

    private async Task AssertClaimAuthorityRolledBackAsync(
        TargetGraph graph, Guid lease, Guid? expectedEndpointId = null)
    {
        await using var verify = fixture.CreateDbContext();
        var target = await verify.WebhookLocalTargetSnapshots.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == graph.Target.Id);
        var endpoint = await verify.WebhookEndpoints.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == graph.Endpoint.Id);
        await Assert.That(target.DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.Delivering);
        await Assert.That(target.ProcessingLeaseToken).IsEqualTo(lease);
        await Assert.That(target.DeliveryFence).IsEqualTo(1);
        await Assert.That(target.ConcurrencyVersion).IsEqualTo(graph.Target.ConcurrencyVersion);
        await Assert.That(target.WebhookEndpointId).IsEqualTo(expectedEndpointId ?? graph.Endpoint.Id);
        await Assert.That(target.WebhookMessageId).IsEqualTo(graph.Message.Id);
        await Assert.That(endpoint.DeliveryStateVersion).IsEqualTo(0);
        await Assert.That(endpoint.ConsecutiveFailureCount).IsEqualTo(0);
        await Assert.That(endpoint.Status).IsEqualTo(WebhookEndpointStatus.Active);
        await Assert.That(endpoint.LastSuccessAt).IsNull();
        await Assert.That(endpoint.LastFailureAt).IsNull();
        await Assert.That(await verify.WebhookDeliveryAttempts.IgnoreQueryFilters()
            .CountAsync(attempt => attempt.TenantId == graph.TenantId)).IsEqualTo(0);
    }
}
