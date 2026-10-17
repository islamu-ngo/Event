
using System.Data.Common;
using System.Net;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
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
    [Arguments(false)]
    [Arguments(true)]
    public async Task HistoricalClaimCrossingDeadlineCannotDiscloseCachedOrStatusZeroRecovery(bool statusZero)
    {
        await using var host = await NativeHost.CreateAsync();
        DateTimeOffset recoverUntil = host.Clock.GetUtcNow().AddSeconds(120).AddHours(24);
        SolvedProof proof = await host.IssueAsync(host.Key);
        using HttpResponseMessage started = await host.StartAsync(host.Key, proof);
        await Assert.That(started.StatusCode).IsEqualTo(HttpStatusCode.Created);
        RegistrationOrder original = (await host.OrdersAsync()).Single();
        RegistrationInventoryHold hold = (await host.HoldsAsync(original.Id)).Single();
        string capability = started.Headers.GetValues("X-Registration-Order-Capability").Single();

        // The ordinary receipt has elapsed, but the original proof still has 119 seconds of recovery.
        // Its operation identity remains fenced; only the feature-owned read may recover it.
        host.Clock.Advance(TimeSpan.FromHours(24).Add(TimeSpan.FromSeconds(1)));
        if (statusZero)
        {
            // Model a lost response completion, not a second execution of the business command.
            await using AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ExploreDbContext>()
                .IdempotencyRecords.Where(record => record.Key == host.Key)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(record => record.StatusCode, IdempotencyRecord.InProgressStatusCode)
                    .SetProperty(record => record.ResponseBody, (string?)null)
                    .SetProperty(record => record.ExpiresAt, recoverUntil.UtcDateTime));
        }
        else
        {
            using HttpResponseMessage replacement = await host.StartAsync(host.Key, proof);
            await Assert.That(replacement.StatusCode).IsEqualTo(HttpStatusCode.Created);
            await Assert.That(string.Equals(replacement.Headers.GetValues("X-Registration-Order-Capability").Single(),
                capability, StringComparison.Ordinal)).IsTrue();
        }

        IdempotencyRecord before = await ReadRecordAsync(host);
        await Assert.That(before.StatusCode).IsEqualTo(statusZero ? 0 : IdempotencyRecord.ResponseUnavailableStatusCode);
        if (statusZero) await Assert.That(before.ExpiresAt).IsEqualTo(recoverUntil.UtcDateTime);
        host.Clock.Advance(recoverUntil - host.Clock.GetUtcNow() - TimeSpan.FromTicks(1));
        var barrier = new ReplayDatabaseBarrier(responseStore: false);
        await using var replica = WithBarrier(host, barrier);
        using HttpClient retryClient = replica.CreateClient();
        barrier.Arm();
        Task<HttpResponseMessage> pending = host.StartAsync(host.Key, proof, client: retryClient);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Assert.That(pending.IsCompleted).IsFalse();
            host.Clock.Advance(TimeSpan.FromTicks(1));
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

    [Test]
    public async Task HistoricalUnavailableRecoveryCrossingDeadlineCannotDiscloseRecoveredCapability()
    {
        await using var host = await NativeHost.CreateAsync();
        DateTimeOffset recoverUntil = host.Clock.GetUtcNow().AddSeconds(120).AddHours(24);
        SolvedProof proof = await host.IssueAsync(host.Key);
        using HttpResponseMessage started = await host.StartAsync(host.Key, proof);
        await Assert.That(started.StatusCode).IsEqualTo(HttpStatusCode.Created);
        RegistrationOrder original = (await host.OrdersAsync()).Single();
        RegistrationInventoryHold hold = (await host.HoldsAsync(original.Id)).Single();
        string capability = started.Headers.GetValues("X-Registration-Order-Capability").Single();
        await using (AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ExploreDbContext>()
                .IdempotencyRecords.Where(record => record.Key == host.Key)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(record => record.StatusCode, IdempotencyRecord.ResponseUnavailableStatusCode)
                    .SetProperty(record => record.ResponseBody, (string?)null)
                    .SetProperty(record => record.ExpiresAt, recoverUntil.UtcDateTime));
        }
        host.Clock.Advance(recoverUntil - host.Clock.GetUtcNow() - TimeSpan.FromTicks(1));
        var barrier = new ReplayDatabaseBarrier(responseStore: false);
        await using var replica = WithBarrier(host, barrier);
        using HttpClient client = replica.CreateClient();
        barrier.Arm();
        Task<HttpResponseMessage> pending = host.StartAsync(host.Key, proof, client: client);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Assert.That(pending.IsCompleted).IsFalse();
            IdempotencyRecord inProgress = await ReadRecordAsync(host);
            await Assert.That(inProgress.StatusCode).IsEqualTo(IdempotencyRecord.ResponseUnavailableStatusCode);
            await Assert.That(inProgress.ExpiresAt).IsEqualTo(recoverUntil.UtcDateTime);
            host.Clock.Advance(TimeSpan.FromTicks(1));
        }
        finally { barrier.Release.TrySetResult(); }
        using HttpResponseMessage denied = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        await AssertDeniedAsync(denied, original.Id, capability);
        await Assert.That((await ReadRecordAsync(host)).ExpiresAt).IsEqualTo(recoverUntil.UtcDateTime);
        await AssertUnchangedAllocationAsync(host, original, hold);
    }

    private static WebApplicationFactory<Program> WithBarrier(NativeHost host, IInterceptor barrier) =>
        host.CreateReplica().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(barrier))));

    private static async Task<IdempotencyRecord> ReadRecordAsync(NativeHost host)
    {
        await using AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<ExploreDbContext>()
            .IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(row =>
                row.Key == host.Key && row.TenantId == PlatformDefaults.DefaultTenantId))!;
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
