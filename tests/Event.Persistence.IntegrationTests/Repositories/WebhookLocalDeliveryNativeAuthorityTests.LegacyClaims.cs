using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class WebhookLocalDeliveryNativeAuthorityTests
{
    [Test]
    public async Task ClaimDue_WithRetryingExecutionStrategy_ClaimsPrimaryTargetAsRetriableUnit()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed);
        await using var global = await authority.Global.CreateDbContextAsync();
        await Assert.That(global.Database.CreateExecutionStrategy().RetriesOnFailure).IsTrue();
        var claim = await ClaimLegacyTargetAsync(authority, rows.First, TimeSpan.FromMinutes(2));
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
        var target = await new WebhookLocalTargetRepository(runtime).GetActiveClaimAsync(
            rows.First.TenantId, rows.First.Target.Id, claim.Token, claim.Fence, Now, CancellationToken.None);
        await Assert.That(target!.Id).IsEqualTo(rows.First.Target.Id);
        await Assert.That(claim.Fence).IsEqualTo(1);
        await Assert.That(target.WebhookMessage.Id).IsEqualTo(rows.First.Message.Id);
    }

    [Test]
    public async Task ClaimDue_WhenWorkersCompete_ProducesOneFencedOwner()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        async Task<IReadOnlyList<WebhookLocalDeliveryClaim>> ClaimAsync()
        {
            await start.Task.WaitAsync(deadline.Token);
            return await authority.Admission.ClaimDueAsync(
                LegacyClaimRequest(rows.First, TimeSpan.FromMinutes(2)),
                LegacyClaimLimits(rows.First), deadline.Token);
        }
        var first = ClaimAsync();
        var second = ClaimAsync();
        start.SetResult();
        var claims = await Task.WhenAll(first, second);
        await Assert.That(claims.Count(items => items.Count == 1)).IsEqualTo(1);
        await Assert.That(claims.Count(items => items.Count == 0)).IsEqualTo(1);
        var owner = claims.SelectMany(items => items).Single();
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
        var targets = new WebhookLocalTargetRepository(runtime);
        await Assert.That(await targets.GetActiveClaimAsync(rows.First.TenantId, rows.First.Target.Id,
            Guid.CreateVersion7(), owner.DeliveryFence, Now, deadline.Token)).IsNull();
        await Assert.That(await targets.GetActiveClaimAsync(rows.First.TenantId, rows.First.Target.Id,
            owner.LeaseToken, owner.DeliveryFence + 1, Now, deadline.Token)).IsNull();
        await Assert.That(await targets.GetActiveClaimAsync(rows.First.TenantId, rows.First.Target.Id,
            owner.LeaseToken, owner.DeliveryFence, Now, deadline.Token)).IsNotNull();
    }

    [Test]
    public async Task GetActiveClaimAsync_WhenEndpointIsArchivedAfterClaim_ReturnsNull()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed);
        var claim = await ClaimLegacyTargetAsync(authority, rows.First, TimeSpan.FromMinutes(2));
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
        await new WebhookEndpointRepository(runtime).ArchiveAsync(
            rows.First.TenantId, rows.First.Endpoint.Id, Now.UtcDateTime, CancellationToken.None);
        await Assert.That(await new WebhookLocalTargetRepository(runtime).GetActiveClaimAsync(
            rows.First.TenantId, rows.First.Target.Id, claim.Token, claim.Fence, Now,
            CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task RecoverExpiredClaim_AppendsFailureEvidenceAndReschedulesTarget()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed);
        var claim = await ClaimLegacyTargetAsync(authority, rows.First, TimeSpan.FromSeconds(1));
        await Assert.That(await authority.Admission.RecoverExpiredClaimsAsync(
            Now.AddSeconds(2), "processing_lease_expired", 10, CancellationToken.None)).IsEqualTo(1);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
        var target = await runtime.WebhookLocalTargetSnapshots.AsNoTracking()
            .SingleAsync(item => item.Id == rows.First.Target.Id);
        var evidence = await runtime.WebhookDeliveryAttempts.AsNoTracking()
            .SingleAsync(item => item.MessageId == rows.First.Message.Id &&
                item.EndpointId == rows.First.Endpoint.Id);
        await Assert.That(target.DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.RetryDue);
        await Assert.That(target.ProcessingLeaseToken).IsNull();
        await Assert.That(evidence.Outcome).IsEqualTo(WebhookDeliveryAttemptOutcome.Failed);
        await Assert.That(evidence.AttemptNumber).IsEqualTo(checked((int)claim.Fence));
        await Assert.That(evidence.FailureCategory).IsEqualTo("processing_lease_expired");
    }

    [Test]
    public async Task RecoverExpiredClaim_WhenRetryBudgetIsExhausted_DeadLettersTarget()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, firstMaxAttempts: 1);
        await using var authority = await CreateAuthorityAsync(seed);
        await ClaimLegacyTargetAsync(authority, rows.First, TimeSpan.FromSeconds(1));
        await authority.Admission.RecoverExpiredClaimsAsync(
            Now.AddSeconds(2), "processing_lease_expired", 10, CancellationToken.None);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
        await Assert.That(await runtime.WebhookLocalTargetSnapshots
            .Where(item => item.Id == rows.First.Target.Id).Select(item => item.DeliveryStatusId).SingleAsync())
            .IsEqualTo((int)WebhookLocalDeliveryStatus.DeadLettered);
    }

    [Test]
    public async Task RecordFailure_WhenClaimLeaseIsStale_DoesNotMutateEndpoint()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed);
        var claim = await ClaimLegacyTargetAsync(authority, rows.First, TimeSpan.FromSeconds(1));
        await authority.Admission.RecoverExpiredClaimsAsync(
            Now.AddSeconds(2), "processing_lease_expired", 10, CancellationToken.None);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
        await new EfCoreUnitOfWork(runtime).ExecuteInTransactionAsync(async token =>
        {
            await Assert.That(await new WebhookEndpointRepository(runtime).RecordFailureAsync(
                rows.First.TenantId, rows.First.Endpoint.Id, rows.First.Target.Id,
                claim.Token, claim.Fence, Now.AddSeconds(2).UtcDateTime, "http_non_success", 2,
                token)).IsNull();
            return true;
        });
        var endpoint = await runtime.WebhookEndpoints.AsNoTracking()
            .SingleAsync(item => item.Id == rows.First.Endpoint.Id);
        await Assert.That(endpoint.ConsecutiveFailureCount).IsEqualTo(0);
        await Assert.That(endpoint.Status).IsEqualTo(WebhookEndpointStatus.Active);
    }

    [Test]
    public async Task RecordFailure_RequiresCurrentTokenAndFence_AndAutoPausesExactlyOnce()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed);
        var claim = await ClaimLegacyTargetAsync(authority, rows.First, TimeSpan.FromMinutes(2));
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
        await new EfCoreUnitOfWork(runtime).ExecuteInTransactionAsync(async cancellation =>
        {
            var endpoints = new WebhookEndpointRepository(runtime);
            Task<WebhookEndpointFailureState?> Fail(Guid token, long fence) => endpoints.RecordFailureAsync(
                rows.First.TenantId, rows.First.Endpoint.Id, rows.First.Target.Id, token, fence,
                Now.UtcDateTime, "http_non_success", 2, cancellation);
            await Assert.That(await Fail(Guid.CreateVersion7(), claim.Fence)).IsNull();
            await Assert.That(await Fail(claim.Token, claim.Fence + 1)).IsNull();
            await Assert.That(await Fail(claim.Token, claim.Fence)).IsEqualTo(new WebhookEndpointFailureState(1, false));
            await Assert.That(await Fail(claim.Token, claim.Fence)).IsEqualTo(new WebhookEndpointFailureState(2, true, true));
            await Assert.That(await Fail(claim.Token, claim.Fence)).IsNull();
            return true;
        });
        var endpoint = await runtime.WebhookEndpoints.AsNoTracking()
            .SingleAsync(item => item.Id == rows.First.Endpoint.Id);
        await Assert.That(endpoint.ConsecutiveFailureCount).IsEqualTo(2);
        await Assert.That(endpoint.Status).IsEqualTo(WebhookEndpointStatus.AutoPaused);
    }

    [Test]
    public async Task EndpointCircuit_AutoPauseAndTenantScopedResumeResetState()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed);
        var claim = await ClaimLegacyTargetAsync(authority, rows.First, TimeSpan.FromMinutes(2));
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
        var endpoints = new WebhookEndpointRepository(runtime);
        await new EfCoreUnitOfWork(runtime).ExecuteInTransactionAsync(async cancellation =>
        {
            await Assert.That(await endpoints.RecordFailureAsync(rows.First.TenantId, rows.First.Endpoint.Id,
                rows.First.Target.Id, claim.Token, claim.Fence, Now.UtcDateTime, "http_non_success", 2,
                cancellation)).IsEqualTo(new WebhookEndpointFailureState(1, false));
            await Assert.That(await endpoints.RecordFailureAsync(rows.First.TenantId, rows.First.Endpoint.Id,
                rows.First.Target.Id, claim.Token, claim.Fence, Now.AddSeconds(1).UtcDateTime, "http_non_success", 2,
                cancellation)).IsEqualTo(new WebhookEndpointFailureState(2, true, true));
            return true;
        });
        string columns = string.Join(", ", new[] { nameof(WebhookEndpoint.LastResumedAt),
            nameof(WebhookEndpoint.LastResumedBy), nameof(WebhookEndpoint.UpdatedBy) }
            .Select(name => Column<WebhookEndpoint>(seed, name)));
        await seed.Database.ExecuteSqlRawAsync(
            $"GRANT UPDATE ({columns}) ON {Table<WebhookEndpoint>(seed)} TO \"{authority.RuntimeRole}\"");
        var actorId = Guid.CreateVersion7();
        try
        {
            await Assert.That(await endpoints.TryResumeAsync(Guid.CreateVersion7(), rows.First.Endpoint.Id,
                2, Now.AddSeconds(2).UtcDateTime, actorId, CancellationToken.None)).IsFalse();
            await Assert.That(await endpoints.TryResumeAsync(rows.First.TenantId, rows.First.Endpoint.Id,
                2, Now.AddSeconds(2).UtcDateTime, actorId, CancellationToken.None)).IsTrue();
        }
        finally
        {
            await seed.Database.ExecuteSqlRawAsync(
                $"REVOKE UPDATE ({columns}) ON {Table<WebhookEndpoint>(seed)} FROM \"{authority.RuntimeRole}\"");
        }
        var endpoint = await runtime.WebhookEndpoints.AsNoTracking()
            .SingleAsync(item => item.Id == rows.First.Endpoint.Id);
        await Assert.That(endpoint.Status).IsEqualTo(WebhookEndpointStatus.Active);
        await Assert.That(endpoint.ConsecutiveFailureCount).IsEqualTo(0);
        await Assert.That(endpoint.CircuitOpenedAt).IsNull();
        await Assert.That(endpoint.AutoPausedAt).IsNull();
        await Assert.That(endpoint.AutoPauseReason).IsNull();
        await Assert.That(endpoint.LastResumedBy).IsEqualTo(actorId);
    }

    private static WebhookLocalTargetClaimRequest LegacyClaimRequest(TargetGraph target, TimeSpan duration) =>
        new(1, 10, 10, [target.TenantId], Now, duration);

    private static Dictionary<Guid, WebhookDeliveryClaimLimits> LegacyClaimLimits(TargetGraph target) =>
        new() { [target.TenantId] = new(10, 1, 1) };

    private static async Task<(Guid Token, long Fence)> ClaimLegacyTargetAsync(
        Authority authority, TargetGraph target, TimeSpan duration)
    {
        var claims = await authority.Admission.ClaimDueAsync(
            LegacyClaimRequest(target, duration), LegacyClaimLimits(target), CancellationToken.None);
        await Assert.That(claims.Count).IsEqualTo(1);
        await Assert.That(claims[0].TargetId).IsEqualTo(target.Target.Id);
        return (claims[0].LeaseToken, claims[0].DeliveryFence);
    }
}
