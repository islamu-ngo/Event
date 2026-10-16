using Explore.Persistence.Database.ProviderPrimitives;
using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class IdempotencyRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task RuntimeClaimPreservesExactRequestReceiptWithoutPrivateTableGrants()
    {
        Guid tenant = Guid.CreateVersion7();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Tenants.Add(new Tenant
            {
                Id = tenant,
                FullName = "Receipt custody",
                Slug = $"receipt-{tenant:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
            await seed.SaveChangesAsync();
        }
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        builder.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        await using var context = new ExploreDbContext(builder.Options)
        {
            TenantContext = new ReceiptTenant(tenant)
        };
        context.BindTenantLease();
        var repository = new IdempotencyRepository(context, enrollment: fixture.NativeQueueEnrollment);
        var original = NewClaim(Guid.CreateVersion7().ToString("N"), tenant,
            DateTime.UtcNow);
        var first = await repository.TryClaimAsync(original);
        using var firstOwner = first.Owner;
        var contender = await repository.TryClaimAsync(NewClaim(original.Key, tenant, original.CreatedAt));
        await Assert.That(first.IsOwner).IsTrue();
        await Assert.That(contender.IsOwner).IsFalse();
        await Assert.That(contender.Record.Id).IsEqualTo(original.Id);
        await Assert.That(contender.Record.RequestBodyHash).IsEqualTo(original.RequestBodyHash);
        await Assert.That(contender.Record.PrincipalFingerprint).IsEqualTo(original.PrincipalFingerprint);
        string table = new IdempotencyReceiptSql(context).Table(typeof(IdempotencyRecord));
        var denied = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync($"SELECT * FROM {table} LIMIT 1"));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
    }

    private sealed record ReceiptTenant(Guid TenantId) : ITenantContext;

    [Test]
    public async Task ScrubExpiredAsyncRetainsFencesUpToBatchSize()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenantId = Guid.NewGuid();
        var now = new DateTime(2026, 5, 28, 12, 0, 0, DateTimeKind.Utc);

        context.IdempotencyRecords.AddRange(
            NewRecord("expired-1", tenantId, now.AddHours(-4)),
            NewRecord("expired-2", tenantId, now.AddHours(-3)),
            NewRecord("expired-3", tenantId, now.AddHours(-2)),
            NewRecord("live", tenantId, now.AddHours(1)));
        await context.SaveChangesAsync();

        var scopes = new IdempotencyCleanupExecutionScopeFactory(
            new TenantExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime), poolSize: 1),
            enrollment: fixture.NativeQueueEnrollment);
        await using var scope = await scopes.CreateAsync(tenantId);
        var eligibleBefore = await scope.CountExpiredAsync(now.AddHours(-1), maximumRows: 2, CancellationToken.None);
        var scrubbed = await scope.ScrubExpiredAsync(now.AddHours(-1), maximumRows: 2, CancellationToken.None);

        await Assert.That(eligibleBefore).IsEqualTo(2);
        await Assert.That(scrubbed).IsEqualTo(2);
        await Assert.That(await context.IdempotencyRecords.CountAsync()).IsEqualTo(4);
        await Assert.That(await context.IdempotencyRecords.CountAsync(record =>
            record.StatusCode == IdempotencyRecord.ResponseUnavailableStatusCode
            && record.ResponseBody == null && record.ContentType == null)).IsEqualTo(2);
        await Assert.That(await context.IdempotencyRecords.AnyAsync(record => record.Key == "live")).IsTrue();
        await Assert.That(await context.IdempotencyRecords.AnyAsync(record => record.Key == "expired-3")).IsTrue();
    }

    [Test]
    public async Task TryClaimAsync_WhenTwoContextsUseTheSameKey_AllowsExactlyOneOwner()
    {
        await fixture.ResetAsync();
        var tenantId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        await using var winnerContext = CreateRuntimeContext(tenantId);
        await using var contenderContext = CreateRuntimeContext(tenantId);
        var winnerRepository = new IdempotencyRepository(winnerContext, enrollment: fixture.NativeQueueEnrollment);
        var contenderRepository = new IdempotencyRepository(contenderContext, enrollment: fixture.NativeQueueEnrollment);
        await using var winnerTransaction = await winnerContext.Database.BeginTransactionAsync();

        var winnerRecord = NewClaim("contended-key", tenantId, now);
        var contenderRecord = NewClaim("contended-key", tenantId, now);
        var winner = await winnerRepository.TryClaimAsync(winnerRecord);
        using var winnerOwner = winner.Owner;
        await Assert.That(winner.IsOwner).IsTrue();

        await contenderContext.Database.OpenConnectionAsync();
        var connection = (Npgsql.NpgsqlConnection)contenderContext.Database.GetDbConnection();
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void ObserveDispatch(object sender, Npgsql.NpgsqlNoticeEventArgs notice)
        {
            if (notice.Notice.MessageText == "receipt_repository_contender_dispatched") dispatched.TrySetResult();
        }
        connection.Notice += ObserveDispatch;
        IdempotencyClaim contender;
        try
        {
            async Task<IdempotencyClaim> DispatchThenClaim()
            {
                await contenderContext.Database.ExecuteSqlRawAsync(
                    "DO $dispatch$ BEGIN RAISE NOTICE 'receipt_repository_contender_dispatched'; END $dispatch$");
                return await contenderRepository.TryClaimAsync(contenderRecord);
            }
            var contenderTask = DispatchThenClaim();
            await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Assert.That(contenderTask.IsCompleted).IsFalse();
            await winnerTransaction.CommitAsync();
            contender = await contenderTask.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally { connection.Notice -= ObserveDispatch; }

        await Assert.That(contender.IsOwner).IsFalse();
        await Assert.That(contender.Record.Id).IsEqualTo(winnerRecord.Id);

        await using var verificationContext = fixture.CreateDbContext();
        await Assert.That(await verificationContext.IdempotencyRecords
            .CountAsync(record => record.Key == "contended-key" && record.TenantId == tenantId)).IsEqualTo(1);
    }

    private ExploreDbContext CreateRuntimeContext(Guid tenant)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        builder.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        var context = new ExploreDbContext(builder.Options) { TenantContext = new ReceiptTenant(tenant) };
        context.BindTenantLease();
        return context;
    }

    private static IdempotencyRecord NewRecord(string key, Guid tenantId, DateTime expiresAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            Key = key,
            TenantId = tenantId,
            UserId = "test-user",
            RequestMethod = "POST",
            RequestTarget = "/api/test",
            RequestBodyHash = Guid.NewGuid().ToString("N"),
            PrincipalFingerprint = Guid.NewGuid().ToString("N"),
            StatusCode = 201,
            ContentType = "application/json",
            ResponseBody = "{}",
            CreatedAt = expiresAt.AddHours(-24),
            ExpiresAt = expiresAt
        };

    private static IdempotencyRecord NewClaim(string key, Guid tenantId, DateTime now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Key = key,
            TenantId = tenantId,
            UserId = "test-user",
            RequestMethod = "POST",
            RequestTarget = "/api/test",
            RequestBodyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("receipt body"u8)),
            PrincipalFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("receipt principal"u8)),
            StatusCode = IdempotencyRecord.InProgressStatusCode,
            CreatedAt = now,
            ExpiresAt = now.AddHours(24)
        };

}
