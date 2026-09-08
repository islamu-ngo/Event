using Explore.API.HealthChecks;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services;
using Explore.Domain;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Privacy;

public sealed class PrivacyErasureReadinessHealthCheckTests
{
    [Test]
    public async Task CaughtUpAuthority_ReturnsBoundedHealthyData()
    {
        IPrivacyErasureReplayCheckpointRepository checkpoints =
            Substitute.For<IPrivacyErasureReplayCheckpointRepository>();
        IPrivacyErasureProviderWorkRepository providerWork =
            Substitute.For<IPrivacyErasureProviderWorkRepository>();
        IOutboxRepository outbox = Substitute.For<IOutboxRepository>();
        IPrivacyErasureAuthority authority = Substitute.For<IPrivacyErasureAuthority>();
        authority.GetStateAsync(Arg.Any<CancellationToken>())
            .Returns(new PrivacyErasureAuthorityState(0, 0));
        authority.ReadAfterAsync(0, 1, Arg.Any<CancellationToken>()).Returns([]);
        providerWork.CountDueAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(2);
        var check = new PrivacyErasureReadinessHealthCheck(
            Options.Create(new PrivacyErasureDurabilityOptions()),
            checkpoints,
            providerWork,
            outbox,
            authority,
            TimeProvider.System);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data.Keys).IsEquivalentTo([
            "topology",
            "restoreReplayProtection",
            "authorityHighWater",
            "authorityRetainedFloor",
            "replayCaughtUp",
            "replayReasonCode",
            "providerDue",
            "providerUnknown",
            "providerDeadLettered",
            "cacheConvergenceIncomplete",
            "cacheConvergenceDeadLettered"]);
        await Assert.That(string.Join('|', result.Data.Values)).DoesNotContain("connection-canary");
    }

    [Test]
    public async Task UnknownOrDeadLetteredProviderWork_DegradesReadinessWithoutIdentifiers()
    {
        IPrivacyErasureAuthority authority = Substitute.For<IPrivacyErasureAuthority>();
        authority.GetStateAsync(Arg.Any<CancellationToken>())
            .Returns(new PrivacyErasureAuthorityState(0, 0));
        authority.ReadAfterAsync(0, 1, Arg.Any<CancellationToken>()).Returns([]);
        IPrivacyErasureProviderWorkRepository providerWork =
            Substitute.For<IPrivacyErasureProviderWorkRepository>();
        providerWork.CountUnknownAsync(Arg.Any<CancellationToken>()).Returns(1);
        providerWork.CountDeadLetteredAsync(Arg.Any<CancellationToken>()).Returns(1);
        var check = new PrivacyErasureReadinessHealthCheck(
            Options.Create(new PrivacyErasureDurabilityOptions()),
            Substitute.For<IPrivacyErasureReplayCheckpointRepository>(),
            providerWork,
            Substitute.For<IOutboxRepository>(),
            authority,
            TimeProvider.System);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Data["providerUnknown"]).IsEqualTo(1);
        await Assert.That(result.Data["providerDeadLettered"]).IsEqualTo(1);
        await Assert.That(string.Join('|', result.Data.Values)).DoesNotContain("provider-canary");
    }

    [Test]
    public async Task AuthorityFailure_ReturnsSanitizedUnhealthyCode()
    {
        IPrivacyErasureAuthority authority = Substitute.For<IPrivacyErasureAuthority>();
        authority.GetStateAsync(Arg.Any<CancellationToken>())
            .Returns<Task<PrivacyErasureAuthorityState>>(_ =>
                throw new InvalidOperationException("connection-canary"));
        var check = new PrivacyErasureReadinessHealthCheck(
            Options.Create(new PrivacyErasureDurabilityOptions()),
            Substitute.For<IPrivacyErasureReplayCheckpointRepository>(),
            Substitute.For<IPrivacyErasureProviderWorkRepository>(),
            Substitute.For<IOutboxRepository>(),
            authority,
            TimeProvider.System);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Description).IsEqualTo("privacy_erasure_authority_unavailable");
        await Assert.That(result.Exception).IsNull();
        await Assert.That(result.Description).DoesNotContain("connection-canary");
    }

    [Test]
    public async Task OutstandingCacheConvergence_DegradesReadinessWithoutIdentifiers()
    {
        IPrivacyErasureAuthority authority = Substitute.For<IPrivacyErasureAuthority>();
        authority.GetStateAsync(Arg.Any<CancellationToken>())
            .Returns(new PrivacyErasureAuthorityState(0, 0));
        authority.ReadAfterAsync(0, 1, Arg.Any<CancellationToken>()).Returns([]);
        IOutboxRepository outbox = Substitute.For<IOutboxRepository>();
        outbox.CountIncompleteByEventTypeAsync(
                PrivacyErasureCacheInvalidationOutboxMessageFactory.EventType,
                Arg.Any<CancellationToken>())
            .Returns(1);
        var check = new PrivacyErasureReadinessHealthCheck(
            Options.Create(new PrivacyErasureDurabilityOptions()),
            Substitute.For<IPrivacyErasureReplayCheckpointRepository>(),
            Substitute.For<IPrivacyErasureProviderWorkRepository>(),
            outbox,
            authority,
            TimeProvider.System);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Data["cacheConvergenceIncomplete"]).IsEqualTo(1);
        await Assert.That(string.Join('|', result.Data.Values)).DoesNotContain("user:");
    }

    [Test]
    public async Task DeadLetteredCacheConvergence_DegradesReadinessWithoutIdentifiers()
    {
        IPrivacyErasureAuthority authority = Substitute.For<IPrivacyErasureAuthority>();
        authority.GetStateAsync(Arg.Any<CancellationToken>())
            .Returns(new PrivacyErasureAuthorityState(0, 0));
        authority.ReadAfterAsync(0, 1, Arg.Any<CancellationToken>()).Returns([]);
        IOutboxRepository outbox = Substitute.For<IOutboxRepository>();
        outbox.CountDeadLetteredByEventTypeAsync(
                PrivacyErasureCacheInvalidationOutboxMessageFactory.EventType,
                Arg.Any<CancellationToken>())
            .Returns(1);
        var check = new PrivacyErasureReadinessHealthCheck(
            Options.Create(new PrivacyErasureDurabilityOptions()),
            Substitute.For<IPrivacyErasureReplayCheckpointRepository>(),
            Substitute.For<IPrivacyErasureProviderWorkRepository>(),
            outbox,
            authority,
            TimeProvider.System);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Data["cacheConvergenceDeadLettered"]).IsEqualTo(1);
        await Assert.That(string.Join('|', result.Data.Values)).DoesNotContain("user:");
    }

    [Test]
    public async Task CheckpointBelowFloor_ReturnsBoundedUnhealthyReasonWithoutReadingFacts()
    {
        IPrivacyErasureAuthority authority = Substitute.For<IPrivacyErasureAuthority>();
        authority.GetStateAsync(Arg.Any<CancellationToken>())
            .Returns(new PrivacyErasureAuthorityState(20, 10));
        IPrivacyErasureReplayCheckpointRepository checkpoints =
            Substitute.For<IPrivacyErasureReplayCheckpointRepository>();
        PrivacyErasureIntent fact = PrivacyErasureIntent.Record(
            Guid.CreateVersion7(),
            5,
            PrivacyErasureSubjectKind.User,
            Guid.CreateVersion7(),
            PrivacyErasureReasonCode.AccountDeletion,
            1,
            DateTime.UtcNow,
            DateTime.UtcNow);
        checkpoints.GetLatestAsync(Arg.Any<CancellationToken>()).Returns(
            PrivacyErasureReplayCheckpoint.Start(
                PrivacyErasureIntent.Record(
                    fact.IntentId,
                    1,
                    fact.SubjectKind,
                    fact.SubjectId,
                    fact.ReasonCode,
                    fact.PolicyVersion,
                    fact.RequestedAtUtc,
                    fact.RecordedAtUtc),
                fact.RecordedAtUtc));
        var check = new PrivacyErasureReadinessHealthCheck(
            Options.Create(new PrivacyErasureDurabilityOptions()),
            checkpoints,
            Substitute.For<IPrivacyErasureProviderWorkRepository>(),
            Substitute.For<IOutboxRepository>(),
            authority,
            TimeProvider.System);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Description).IsEqualTo("stale_restore_below_retained_floor");
        await authority.DidNotReceive().ReadAfterAsync(
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }
}
