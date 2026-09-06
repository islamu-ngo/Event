// ABOUTME: Verifies transient-store and cleanup telemetry through real API and persistence operations.
// ABOUTME: Rejects locator, assertion, payload and caller-controlled values in native metric dimensions.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Explore.API.Scheduling;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Quartz;

namespace Event.API.IntegrationTests.Authentication;

[ClassDataSource<AtprotoTransientApiFixture>(Shared = SharedType.PerClass)]
[NotInParallel("AtprotoTransientApi")]
public sealed class AtprotoTransientTelemetryTests(AtprotoTransientApiFixture fixture)
{
    [Test]
    public async Task OperationMetricsContainOnlyClosedDimensionsForSuccessfulAndRejectedAssertions()
    {
        var observed = new ConcurrentQueue<Observation>();
        using var listener = Listen(observed);
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new { purpose = "health_probe" });
        using var request = fixture.Request(body, fixture.Sign(body, "probe"), "probe");
        using var success = await fixture.Client.SendAsync(request);
        await Assert.That(success.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        string canary = "untrusted-purpose-" + Guid.CreateVersion7().ToString("N");
        byte[] badBody = JsonSerializer.SerializeToUtf8Bytes(new { purpose = canary });
        using var invalid = fixture.Request(badBody, fixture.Sign(badBody, "probe"), "probe");
        using var denied = await fixture.Client.SendAsync(invalid);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(observed.Any(entry => entry.Name == "explore.atproto.transient.operations"
            && Equals(entry.Tags["operation"], "probe") && Equals(entry.Tags["purpose"], "health_probe")
            && Equals(entry.Tags["outcome"], "succeeded"))).IsTrue();
        await Assert.That(observed.Any(entry => entry.Name == "explore.atproto.transient.operations"
            && Equals(entry.Tags["outcome"], "rejected"))).IsTrue();
        await Assert.That(observed.All(entry => entry.Tags.Keys.All(key => key is "operation" or "purpose" or "outcome")
            && entry.Tags.Values.All(value => value?.ToString()?.Contains(canary, StringComparison.Ordinal) != true))).IsTrue();
    }

    [Test]
    public async Task ScheduledCleanupReportsRealDeletedRowsWithoutRecordOrTenantLabels()
    {
        var observed = new ConcurrentQueue<Observation>();
        using var listener = Listen(observed);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        long expired = fixture.Clock.GetUtcNow().AddMilliseconds(-1).ToUnixTimeMilliseconds();
        db.AtprotoTransientRecords.Add(AtprotoTransientRecord.CreateHealthProbe(AtprotoTransientApiFixture.NewDigest(), "synthetic", expired));
        db.AtprotoTransientAssertionReplays.Add(AtprotoTransientAssertionReplay.CreateFromAssertionId(Guid.CreateVersion7().ToString("D"), expired - 10_000));
        await db.SaveChangesAsync();
        var job = ActivatorUtilities.CreateInstance<AtprotoTransientCleanupJob>(scope.ServiceProvider);
        // Only Quartz's delivery context is substituted; the cleanup service and both repositories are real.
        var context = Substitute.For<IJobExecutionContext>();
        await job.Execute(context);
        await Assert.That(observed.Any(entry => entry.Name == "explore.atproto.transient.cleanup_rows"
            && entry.Count == 1 && Equals(entry.Tags["store"], "transients"))).IsTrue();
        await Assert.That(observed.Any(entry => entry.Name == "explore.atproto.transient.cleanup_rows"
            && entry.Count == 1 && Equals(entry.Tags["store"], "assertions"))).IsTrue();
        await Assert.That(observed.Any(entry => entry.Name == "explore.atproto.transient.cleanup_runs"
            && Equals(entry.Tags["outcome"], "succeeded"))).IsTrue();
        await Assert.That(observed.All(entry => entry.Tags.Keys.All(key => key is "store" or "outcome"))).IsTrue();
    }

    [Test]
    public async Task PartialCleanupFailureReportsOnlyFailureWithoutClaimingCompletedRowTotals()
    {
        var observed = new ConcurrentQueue<Observation>();
        using var listener = Listen(observed);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        db.AtprotoTransientRecords.Add(AtprotoTransientRecord.CreateHealthProbe(AtprotoTransientApiFixture.NewDigest(),
            "synthetic", fixture.Clock.GetUtcNow().AddMilliseconds(-1).ToUnixTimeMilliseconds()));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE islamu_event.atproto_transient_assertion_replays RENAME TO telemetry_replay_unavailable");
        bool failed = false;
        try
        {
            var job = ActivatorUtilities.CreateInstance<AtprotoTransientCleanupJob>(scope.ServiceProvider);
            try { await job.Execute(Substitute.For<IJobExecutionContext>()); }
            catch (Exception) { failed = true; }
            await Assert.That(failed).IsTrue();
            await Assert.That(await db.AtprotoTransientRecords.CountAsync()).IsEqualTo(0);
            await Assert.That(observed.Any(entry => entry.Name == "explore.atproto.transient.cleanup_runs"
                && Equals(entry.Tags["outcome"], "failed"))).IsTrue();
            await Assert.That(observed.Any(entry => entry.Name == "explore.atproto.transient.cleanup_rows")).IsFalse();
            await Assert.That(observed.All(entry => entry.Tags.Keys.All(key => key == "outcome"))).IsTrue();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE islamu_event.telemetry_replay_unavailable RENAME TO atproto_transient_assertion_replays");
        }
    }

    private static MeterListener Listen(ConcurrentQueue<Observation> observations)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, target) =>
            {
                if (instrument.Meter.Name == "Explore.Business"
                    && instrument.Name.StartsWith("explore.atproto.transient.", StringComparison.Ordinal))
                    target.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => observations.Enqueue(
            new(instrument.Name, value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value))));
        listener.Start();
        return listener;
    }

    private sealed record Observation(string Name, long Count, IReadOnlyDictionary<string, object?> Tags);
}
