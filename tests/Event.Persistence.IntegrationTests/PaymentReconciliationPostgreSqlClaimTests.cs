using System.Data.Common;
using System.Diagnostics;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PaymentReconciliationPostgreSqlClaimTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ClaimDueUsesOnePostgreSqlCommandAndStableBoundedFiftyRowStrategy()
    {
        await fixture.ResetAsync();
        var counter = new CommandCounter();
        var baseline = new PersistenceQueryBaselineInterceptor();
        await using ExploreDbContext context = fixture.CreateDbContext(counter, baseline);
        await SeedDueRowsWithoutParentFixturesAsync(context, 55);
        counter.Reset();
        baseline.Reset();
        var repository = new RegistrationPaymentAttemptRepository(context);

        var elapsed = Stopwatch.StartNew();
        var first = await repository.ClaimDueReconciliationsAsync(
            "postgres-command-proof", 50, Now, TimeSpan.FromMinutes(2), CancellationToken.None);
        elapsed.Stop();
        PersistenceQueryBaselineEvidence.Record(baseline
            .Snapshot("payment_reconciliation_claim", first.Count, elapsed.Elapsed));

        await Assert.That(first.Count).IsEqualTo(50);
        await Assert.That(first.Select(claim => claim.AttemptCount).All(count => count == 1)).IsTrue();
        await Assert.That(counter.CommandCount).IsEqualTo(1);
        counter.Reset();
        var second = await repository.ClaimDueReconciliationsAsync(
            "postgres-command-proof", 50, Now, TimeSpan.FromMinutes(2), CancellationToken.None);
        await Assert.That(second.Count).IsEqualTo(5);
        await Assert.That(counter.CommandCount).IsEqualTo(1);
    }

    [Test]
    public async Task ThousandDueRowsAreClaimedWithinTenMinuteRecoveryBudget()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        await SeedDueRowsWithoutParentFixturesAsync(context, 1_000);
        var repository = new RegistrationPaymentAttemptRepository(context);
        var elapsed = Stopwatch.StartNew();
        var claimedIds = new HashSet<Guid>();

        for (int batch = 0; batch < 20; batch++)
        {
            IReadOnlyList<Explore.Application.Contracts.Persistence.PaymentReconciliationClaim> claims =
                await repository.ClaimDueReconciliationsAsync(
                    "postgres-throughput-proof",
                    50,
                    Now,
                    TimeSpan.FromMinutes(2),
                    CancellationToken.None);

            await Assert.That(claims.Count).IsEqualTo(50);
            foreach (Explore.Application.Contracts.Persistence.PaymentReconciliationClaim claim in claims)
            {
                await Assert.That(claimedIds.Add(claim.EffectId)).IsTrue();
            }
        }

        elapsed.Stop();
        await Assert.That(claimedIds.Count).IsEqualTo(1_000);
        await Assert.That(elapsed.Elapsed).IsLessThan(TimeSpan.FromMinutes(10));
        await Assert.That((await repository.ClaimDueReconciliationsAsync(
            "postgres-throughput-proof",
            50,
            Now,
            TimeSpan.FromMinutes(2),
            CancellationToken.None)).Count).IsEqualTo(0);
    }

    private static async Task SeedDueRowsWithoutParentFixturesAsync(ExploreDbContext context, int count)
    {
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET session_replication_role = replica;");
        Guid tenantId = Guid.CreateVersion7();
        string values = string.Join(",", Enumerable.Range(0, count).Select(index =>
        {
            Guid id = Guid.CreateVersion7();
            Guid orderId = Guid.CreateVersion7();
            Guid attemptId = Guid.CreateVersion7();
            DateTime due = Now.AddMinutes(-count + index);
            return $"('{id:D}'::uuid,'{tenantId:D}'::uuid,'{orderId:D}'::uuid,'{attemptId:D}'::uuid,1,0,0,'{due:O}'::timestamptz,'{due:O}'::timestamptz)";
        }));
        await context.Database.ExecuteSqlRawAsync($$"""
            INSERT INTO payment_reconciliation_effects
                (id, tenant_id, registration_order_id, payment_attempt_id, status, attempt_count,
                 processing_fence, next_attempt_at, created_at)
            VALUES {{values}};
            """);
        await context.Database.ExecuteSqlRawAsync("SET session_replication_role = origin;");
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int CommandCount { get; private set; }
        public void Reset() => CommandCount = 0;
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            CommandCount++;
            return result;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CommandCount++;
            return ValueTask.FromResult(result);
        }
        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            CommandCount++;
            return result;
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            CommandCount++;
            return ValueTask.FromResult(result);
        }
    }
}
