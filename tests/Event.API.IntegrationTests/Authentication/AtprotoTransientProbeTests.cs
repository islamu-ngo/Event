// ABOUTME: Exercises the private synthetic storage probe through real machine authentication and PostgreSQL.
// ABOUTME: Guards tenantless bounded round trips, purpose isolation, single-use assertions and redacted outages.

extern alias bff;

using System.Net;
using System.Text.Json;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.API.IntegrationTests.Authentication;

[ClassDataSource<AtprotoTransientApiFixture>(Shared = SharedType.PerClass)]
[NotInParallel("AtprotoTransientApi")]
public sealed class AtprotoTransientProbeTests(AtprotoTransientApiFixture fixture)
{
    private static readonly byte[] ProbeBody = JsonSerializer.SerializeToUtf8Bytes(new { purpose = "health_probe" });

    [Test]
    public async Task SignedProbeCompletesWithoutTenantOrPayload_AndAssertionCannotBeReplayed()
    {
        string assertion = Sign(ProbeBody);
        using var request = fixture.Request(ProbeBody, assertion, "probe");
        using var response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo(string.Empty);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await db.AtprotoTransientRecords.CountAsync(row => row.Purpose == AtprotoTransientPurpose.HealthProbe)).IsEqualTo(0);
        using var replay = fixture.Request(ProbeBody, assertion, "probe");
        using var rejected = await fixture.Client.SendAsync(replay);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    [Arguments("create")]
    [Arguments("read")]
    [Arguments("consume")]
    public async Task ProbePurposeCannotReachAuthenticationRecordOperations(string operation)
    {
        using var request = fixture.Request(ProbeBody, Sign(ProbeBody, operation), operation);
        using var response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ProbeRejectsAnonymousRequestsAndCallerSuppliedTenantOrLocator()
    {
        using var anonymous = fixture.Request(ProbeBody, null, "probe");
        using var denied = await fixture.Client.SendAsync(anonymous);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        byte[][] attacks =
        [
            JsonSerializer.SerializeToUtf8Bytes(new { purpose = "health_probe", tenantId = Guid.CreateVersion7() }),
            JsonSerializer.SerializeToUtf8Bytes(new { purpose = "health_probe", tokenDigest = new string('a', 64) }),
            JsonSerializer.SerializeToUtf8Bytes(new { purpose = "oauth_state" })
        ];
        foreach (byte[] body in attacks)
        {
            using var request = fixture.Request(body, Sign(body), "probe");
            using var response = await fixture.Client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
    }

    [Test]
    public async Task MissingStorageReturnsNoDiagnostics_ThenFreshProbeRecovers()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE islamu_event.atproto_transient_records RENAME TO transient_probe_unavailable");
        string assertion = Sign(ProbeBody);
        try
        {
            using var request = fixture.Request(ProbeBody, assertion, "probe");
            using var response = await fixture.Client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            string body = await response.Content.ReadAsStringAsync();
            await Assert.That(body.Contains("transient_probe_unavailable", StringComparison.Ordinal)).IsFalse();
            await Assert.That(body.Contains(assertion, StringComparison.Ordinal)).IsFalse();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE islamu_event.transient_probe_unavailable RENAME TO atproto_transient_records");
        }
        using var fresh = fixture.Request(ProbeBody, Sign(ProbeBody), "probe");
        using var recovered = await fixture.Client.SendAsync(fresh);
        await Assert.That(recovered.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    private string Sign(byte[] body, string operation = "probe") =>
        fixture.Sign(body, operation, claims => claims["purpose"] = "health_probe");

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LostProbeResponseIsNotRetriedByGlobalResilience(bool globalHedging)
    {
        using var loss = new LoseProbeResponseHandler { InnerHandler = fixture.Factory.Server.CreateHandler() };
        await using var services = await fixture.CreateBffServicesAsync(loss, globalHedging: globalHedging);
        var store = services.GetRequiredService<bff::Explore.Blazor.Services.Auth.ApiBackedAtprotoTransientStore>();
        await Assert.That(async () => await store.ProbeAsync()).Throws<HttpRequestException>();
        await Assert.That(loss.Attempts).IsEqualTo(1);
        await using var healthyServices = await fixture.CreateBffServicesAsync();
        await Assert.That(await healthyServices.GetRequiredService<bff::Explore.Blazor.Services.Auth.ApiBackedAtprotoTransientStore>()
            .ProbeAsync()).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProbeWaitIsBoundedByTwoSecondBudgetAndCallerCancellation(bool cancelCaller)
    {
        var clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(_ => fixture.Clock.GetUtcNow());
        var timerCreated = new TaskCompletionSource<(TimerCallback Callback, object? State, TimeSpan DueTime)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        clock.CreateTimer(Arg.Any<TimerCallback>(), Arg.Any<object?>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>())
            .Returns(call =>
            {
                timerCreated.TrySetResult((call.ArgAt<TimerCallback>(0), call.ArgAt<object?>(1), call.ArgAt<TimeSpan>(2)));
                return Substitute.For<ITimer>();
            });
        using var stall = new StallProbeHandler();
        await using var services = await fixture.CreateBffServicesAsync(stall, timeProvider: clock);
        using var cancellation = new CancellationTokenSource();
        var probe = services.GetRequiredService<bff::Explore.Blazor.Services.Auth.ApiBackedAtprotoTransientStore>()
            .ProbeAsync(cancellation.Token);
        try
        {
            // Wall-clock waits guard hangs; the captured native timer proves the exact deadline.
            await stall.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Assert.That(timerCreated.Task.IsCompletedSuccessfully).IsTrue();
            var timer = await timerCreated.Task;
            await Assert.That(timer.DueTime).IsEqualTo(TimeSpan.FromSeconds(2));
            await Assert.That(stall.RequestCancellation.IsCancellationRequested).IsFalse();
            if (cancelCaller) cancellation.Cancel();
            else timer.Callback(timer.State);
            await Assert.That(stall.RequestCancellation.IsCancellationRequested).IsTrue();
            await Assert.That(async () => await probe.WaitAsync(TimeSpan.FromSeconds(30))).Throws<OperationCanceledException>();
            await Assert.That(stall.Attempts).IsEqualTo(1);
        }
        finally
        {
            cancellation.Cancel();
            try { await probe.WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (OperationCanceledException) { }
        }
    }

    [Test]
    public async Task FailedProbeConsumptionLeavesOnlyAnExpiringTenantlessSyntheticRecord()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION islamu_event.reject_test_probe_delete() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF OLD.purpose = 3 THEN RAISE EXCEPTION 'Synthetic probe deletion unavailable'; END IF;
                RETURN OLD;
            END $$;
            CREATE TRIGGER reject_test_probe_delete BEFORE DELETE ON islamu_event.atproto_transient_records
                FOR EACH ROW EXECUTE FUNCTION islamu_event.reject_test_probe_delete();
            """);
        try
        {
            using var request = fixture.Request(ProbeBody, Sign(ProbeBody), "probe");
            using var response = await fixture.Client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            var row = await db.AtprotoTransientRecords.AsNoTracking()
                .SingleAsync(row => row.Purpose == AtprotoTransientPurpose.HealthProbe);
            await Assert.That(row.TenantId).IsNull();
            await Assert.That(row.ExpiresAtUnixMilliseconds).IsEqualTo(fixture.Clock.GetUtcNow().AddSeconds(30).ToUnixTimeMilliseconds());
            await Assert.That(Convert.FromBase64String(row.ProtectedPayload).Length).IsEqualTo(32);
            string output = await response.Content.ReadAsStringAsync();
            await Assert.That(output.Contains(row.ProtectedPayload, StringComparison.Ordinal)).IsFalse();
            await Assert.That(output.Contains(row.TokenDigest, StringComparison.Ordinal)).IsFalse();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_test_probe_delete ON islamu_event.atproto_transient_records");
            var expiredClock = new ProbeExpiryClock(fixture.Clock.GetUtcNow().AddSeconds(30));
            var transients = new AtprotoTransientStoreRepository(db, expiredClock);
            await Assert.That(await transients.ReadHealthProbeAsync(row.Id, row.TokenDigest)).IsNull();
            var cleanup = new AtprotoTransientCleanupService(transients,
                new AtprotoTransientAssertionReplayRepository(db, expiredClock), expiredClock);
            await Assert.That((await cleanup.CleanupExpiredAsync()).TransientRows).IsEqualTo(1);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER IF EXISTS reject_test_probe_delete ON islamu_event.atproto_transient_records;
                DROP FUNCTION IF EXISTS islamu_event.reject_test_probe_delete();
                """);
            await db.AtprotoTransientRecords.Where(row => row.Purpose == AtprotoTransientPurpose.HealthProbe).ExecuteDeleteAsync();
        }
    }

    private sealed class ProbeExpiryClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class LoseProbeResponseHandler : DelegatingHandler
    {
        public int Attempts { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            var response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                response.Dispose();
                throw new HttpRequestException("Probe response lost after commit.");
            }
            return response;
        }
    }

    private sealed class StallProbeHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Attempts { get; private set; }
        public CancellationToken RequestCancellation { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            RequestCancellation = cancellationToken;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Stalled transport resumed without cancellation.");
        }
    }
}
