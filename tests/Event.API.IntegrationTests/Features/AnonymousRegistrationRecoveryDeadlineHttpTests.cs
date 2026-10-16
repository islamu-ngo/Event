
using System.Data.Common;
using System.Net;
using System.Text.Json;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static Event.Api.IntegrationTests.Features.AnonymousRegistrationChallengeHttpTests;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class AnonymousRegistrationRecoveryDeadlineHttpTests
{
    [Test]
    [Arguments(201)]
    [Arguments(IdempotencyRecord.InProgressStatusCode)]
    [Arguments(IdempotencyRecord.ResponseUnavailableStatusCode)]
    public async Task HistoricalClaimCrossingDeadlineCannotDiscloseCachedOrIncompleteRecovery(int receiptStatus)
    {
        await using var host = await NativeHost.CreateAsync();
        DateTimeOffset recoverUntil = host.Clock.GetUtcNow().AddSeconds(120).AddHours(24);
        SolvedProof proof = await host.IssueAsync(host.Key);
        var completion = new ReplayDatabaseBarrier(responseStore: true);
        await using var ownerReplica = WithBarrier(host, completion);
        using HttpClient ownerClient = ownerReplica.CreateClient();
        if (receiptStatus == IdempotencyRecord.InProgressStatusCode)
            completion.Arm();
        Task<HttpResponseMessage> owner = host.StartAsync(host.Key, proof, client: ownerClient);
        try
        {
            if (receiptStatus == IdempotencyRecord.InProgressStatusCode)
                await completion.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            else
            {
                HttpResponseMessage started = await owner.WaitAsync(TimeSpan.FromSeconds(30));
                await Assert.That(started.StatusCode).IsEqualTo(HttpStatusCode.Created);
            }
            RegistrationOrder original = (await host.OrdersAsync()).Single();
            RegistrationInventoryHold hold = (await host.HoldsAsync(original.Id)).Single();
            IdempotencyRecord initial = await ReadRecordAsync(host);
            await Assert.That(initial.ExpiresAt).IsEqualTo(initial.CreatedAt.AddHours(24));

            if (receiptStatus == IdempotencyRecord.ResponseUnavailableStatusCode)
                host.Clock.Advance(TimeSpan.FromHours(24).Add(TimeSpan.FromSeconds(1)));
            using HttpResponseMessage recovered = await host.StartAsync(host.Key, proof);
            await Assert.That(recovered.StatusCode).IsEqualTo(HttpStatusCode.Created);
            string capability = recovered.Headers.GetValues("X-Registration-Order-Capability").Single();

            IdempotencyRecord before = await ReadRecordAsync(host);
            await Assert.That(before.Id).IsEqualTo(initial.Id);
            await Assert.That(before.CreatedAt).IsEqualTo(initial.CreatedAt);
            await Assert.That(before.ExpiresAt).IsEqualTo(initial.ExpiresAt);
            await Assert.That(before.StatusCode).IsEqualTo(receiptStatus);
            if (receiptStatus == 201)
                await Assert.That(before.ResponseBody!).StartsWith("dp:v1:");
            else
                await Assert.That(before.ResponseBody).IsNull();

            // A cached response or live original claim must be read before cache expiry.
            // An expired receipt instead stays scrubbed; recovery never renews its lease.
            DateTimeOffset readAt = receiptStatus == IdempotencyRecord.ResponseUnavailableStatusCode
                ? recoverUntil.AddTicks(-1)
                : new DateTimeOffset(before.ExpiresAt, TimeSpan.Zero).AddTicks(-1);
            host.Clock.Advance(readAt - host.Clock.GetUtcNow());
            var barrier = new ReplayDatabaseBarrier(responseStore: false);
            await using var replica = WithBarrier(host, barrier);
            using HttpClient retryClient = replica.CreateClient();
            barrier.Arm();
            Task<HttpResponseMessage> pending = host.StartAsync(host.Key, proof, client: retryClient);
            try
            {
                await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                await Assert.That(pending.IsCompleted).IsFalse();
                host.Clock.Advance(recoverUntil - host.Clock.GetUtcNow());
                await Assert.That(host.Clock.GetUtcNow()).IsEqualTo(recoverUntil);
            }
            finally { barrier.Release.TrySetResult(); }
            using HttpResponseMessage denied = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            await AssertDeniedAsync(denied, original.Id, capability);
            IdempotencyRecord after = await ReadRecordAsync(host);
            await Assert.That(after.Id).IsEqualTo(before.Id);
            await Assert.That(after.CreatedAt).IsEqualTo(before.CreatedAt);
            await Assert.That(after.ExpiresAt).IsEqualTo(before.ExpiresAt);
            await Assert.That(after.StatusCode).IsEqualTo(before.StatusCode);
            await Assert.That(after.ResponseBody == before.ResponseBody).IsTrue();
            await AssertUnchangedAllocationAsync(host, original, hold);
        }
        finally
        {
            completion.Release.TrySetResult();
            using HttpResponseMessage completed = await owner.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    [Test]
    public async Task OriginalResponseCompletionCrossingDeadlineCannotDiscloseCommittedCapability()
    {
        await using var host = await NativeHost.CreateAsync();
        DateTimeOffset recoverUntil = host.Clock.GetUtcNow().AddSeconds(120).AddHours(24);
        SolvedProof proof = await host.IssueAsync(host.Key);
        var barrier = new ReplayDatabaseBarrier(responseStore: true);
        await using var replica = WithBarrier(host, barrier);
        using HttpClient client = replica.CreateClient();
        barrier.Arm();
        Task<HttpResponseMessage> pending = host.StartAsync(host.Key, proof, client: client);
        RegistrationOrder original;
        RegistrationInventoryHold hold;
        string capability;
        IdempotencyRecord inProgress;
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Assert.That(pending.IsCompleted).IsFalse();
            original = (await host.OrdersAsync()).Single();
            hold = (await host.HoldsAsync(original.Id)).Single();
            using HttpResponseMessage recovered = await host.StartAsync(host.Key, proof);
            await Assert.That(recovered.StatusCode).IsEqualTo(HttpStatusCode.Created);
            capability = recovered.Headers.GetValues("X-Registration-Order-Capability").Single();
            inProgress = await ReadRecordAsync(host);
            await Assert.That(inProgress.StatusCode).IsEqualTo(0);
            await Assert.That(inProgress.ResponseBody).IsNull();
            await Assert.That(inProgress.ExpiresAt).IsEqualTo(inProgress.CreatedAt.AddHours(24));
            host.Clock.Advance(recoverUntil - host.Clock.GetUtcNow());
            await Assert.That(host.Clock.GetUtcNow()).IsEqualTo(recoverUntil);
        }
        finally { barrier.Release.TrySetResult(); }
        using HttpResponseMessage denied = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        await AssertDeniedAsync(denied, original.Id, capability);
        IdempotencyRecord after = await ReadRecordAsync(host);
        await Assert.That(after.Id).IsEqualTo(inProgress.Id);
        await Assert.That(after.CreatedAt).IsEqualTo(inProgress.CreatedAt);
        await Assert.That(after.ExpiresAt).IsEqualTo(inProgress.ExpiresAt);
        await Assert.That(after.StatusCode).IsEqualTo(201);
        await Assert.That(after.ResponseBody!).StartsWith("dp:v1:");
        await Assert.That(after.ResponseBody!.Contains(capability, StringComparison.Ordinal)).IsFalse();
        await AssertUnchangedAllocationAsync(host, original, hold);
    }

    private static WebApplicationFactory<Program> WithBarrier(NativeHost host, IInterceptor barrier) =>
        host.CreateReplica().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(barrier))));

    private static async Task<IdempotencyRecord> ReadRecordAsync(NativeHost host)
    {
        await using AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ExploreDbContext>()
            .IdempotencyRecords.AsNoTracking().SingleAsync(record =>
                record.Key == host.Key && record.TenantId == PlatformDefaults.DefaultTenantId);
    }

    private static async Task AssertDeniedAsync(HttpResponseMessage response, Guid orderId, string capability)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Headers.Contains("X-Registration-Order-Capability")).IsFalse();
        await Assert.That(response.Headers.Contains("X-Idempotency-Replay")).IsFalse();
        await Assert.That(response.Headers.Location).IsNull();
        string body = await response.Content.ReadAsStringAsync();
        await Assert.That(body.Contains(orderId.ToString("D"), StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(body.Contains(capability, StringComparison.Ordinal)).IsFalse();
        using JsonDocument document = JsonDocument.Parse(body);
        await Assert.That(document.RootElement.GetProperty("code").GetString())
            .IsEqualTo("anonymous_registration_challenge_invalid");
    }

    private static async Task AssertUnchangedAllocationAsync(NativeHost host, RegistrationOrder original,
        RegistrationInventoryHold originalHold)
    {
        RegistrationOrder order = (await host.OrdersAsync()).Single();
        RegistrationInventoryHold hold = (await host.HoldsAsync(order.Id)).Single();
        await Assert.That(order.Id).IsEqualTo(original.Id);
        await Assert.That(order.CreatedAt).IsEqualTo(original.CreatedAt);
        await Assert.That(order.ExpiresAt).IsEqualTo(original.ExpiresAt);
        await Assert.That(order.ConcurrencyStamp).IsEqualTo(original.ConcurrencyStamp);
        await Assert.That(hold.Id).IsEqualTo(originalHold.Id);
        await Assert.That(hold.ExpiresAt).IsEqualTo(originalHold.ExpiresAt);
        await Assert.That(hold.ConcurrencyStamp).IsEqualTo(originalHold.ConcurrencyStamp);
        await Assert.That(hold.Quantity).IsEqualTo(originalHold.Quantity);
    }

    private sealed class ReplayDatabaseBarrier(bool responseStore) : DbCommandInterceptor
    {
        private int _armed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await PauseAsync(command, eventData, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await PauseAsync(command, eventData, cancellationToken);
            return result;
        }

        private async Task PauseAsync(DbCommand command, CommandEventData eventData, CancellationToken cancellationToken)
        {
            string table = eventData.Context!.Model.FindEntityType(typeof(IdempotencyRecord))!.GetTableName()!;
            if (!command.CommandText.StartsWith(responseStore ? "UPDATE " : "SELECT ", StringComparison.Ordinal)
                || !command.CommandText.Contains('"' + table + '"', StringComparison.Ordinal)
                || Interlocked.CompareExchange(ref _armed, 0, 1) != 1)
                return;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }
}
