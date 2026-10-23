using Explore.Persistence.Database.ProviderPrimitives;
using System.Data.Common;
using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteIdempotencyClaims")]
public sealed class IdempotencyRepositorySqliteTests
{
    private static readonly DateTime Now =
        DateTime.SpecifyKind(new DateTime(2026, 8, 27, 12, 0, 0), DateTimeKind.Utc);

    [Test]
    public async Task SubjectErasureScrubsBothTenantReceiptsAndRollbackRestoresTheirIdentity()
    {
        string path = Path.Combine(Path.GetTempPath(), $"receipt-erasure-{Guid.CreateVersion7():N}.db");
        Guid tenant = Guid.CreateVersion7(), otherTenant = Guid.CreateVersion7();
        Guid subject = Guid.CreateVersion7(), unrelated = Guid.CreateVersion7();
        var receipts = new[]
        {
            NewClaim("subject-d", tenant), NewClaim("subject-n", otherTenant),
            NewClaim("unrelated", tenant)
        };
        receipts[0].UserId = subject.ToString("D");
        receipts[1].UserId = subject.ToString("N");
        receipts[2].UserId = unrelated.ToString("D");
        foreach (var receipt in receipts)
        {
            receipt.StatusCode = 201;
            receipt.ResponseBody = "private response";
            receipt.ContentType = "text/plain";
            receipt.RequestContentType = "application/json";
        }
        try
        {
            await using (var setup = CreateContext(path))
            {
                await setup.Database.EnsureCreatedAsync();
                await SqliteDatabaseInitializer.InitializeAsync(setup, CancellationToken.None);
                setup.AddRange(receipts);
                await setup.SaveChangesAsync();
            }
            await using (var selected = CreateContext(path, tenant))
            await using (var transaction = await selected.Database.BeginTransactionAsync())
            {
                await new UserLocationPrivacyErasureRepository(selected)
                    .EraseRegistrationAndLocalNotificationsAsync(subject, CancellationToken.None);
                var rows = await selected.IdempotencyRecords.IgnoreQueryFilters().AsNoTracking().ToArrayAsync();
                foreach (var original in receipts)
                {
                    var row = rows.Single(value => value.Id == original.Id);
                    bool erased = original.UserId != unrelated.ToString("D");
                    await Assert.That(row.Key).IsEqualTo(original.Key);
                    await Assert.That(row.CreatedAt).IsEqualTo(original.CreatedAt);
                    await Assert.That(row.ExpiresAt).IsEqualTo(original.ExpiresAt);
                    await Assert.That(row.StatusCode).IsEqualTo(erased ? -1 : 201);
                    await Assert.That(row.UserId).IsEqualTo(erased ? null : original.UserId);
                    await Assert.That(row.ResponseBody).IsEqualTo(erased ? null : original.ResponseBody);
                    await Assert.That(row.ContentType).IsEqualTo(erased ? null : original.ContentType);
                    await Assert.That(row.RequestMethod).IsEqualTo(erased ? "" : original.RequestMethod);
                    await Assert.That(row.RequestTarget).IsEqualTo(erased ? "" : original.RequestTarget);
                    await Assert.That(row.RequestContentType).IsEqualTo(erased ? null : original.RequestContentType);
                    await Assert.That(row.RequestBodyHash).IsEqualTo(erased ? "" : original.RequestBodyHash);
                    await Assert.That(row.PrincipalFingerprint).IsEqualTo(erased ? "" : original.PrincipalFingerprint);
                }
                await transaction.RollbackAsync();
            }
            await using var verification = CreateContext(path);
            var restored = await verification.IdempotencyRecords.AsNoTracking().ToArrayAsync();
            foreach (var original in receipts)
            {
                var row = restored.Single(value => value.Id == original.Id);
                await Assert.That(row.StatusCode).IsEqualTo(201);
                await Assert.That(row.UserId).IsEqualTo(original.UserId);
                await Assert.That(row.ResponseBody).IsEqualTo(original.ResponseBody);
                await Assert.That(row.PrincipalFingerprint).IsEqualTo(original.PrincipalFingerprint);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            File.Delete(path + "-shm");
            File.Delete(path + "-wal");
        }
    }

    [Test]
    public async Task ConcurrentSameKeyClaims_ProduceOneOwnerAndOneDurableRecord()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"idempotency-race-{Guid.CreateVersion7():N}.db");
        try
        {
            await using (ExploreDbContext setup = CreateContext(databasePath))
            {
                await setup.Database.EnsureCreatedAsync();
                await SqliteDatabaseInitializer.InitializeAsync(setup, CancellationToken.None);
            }

            Guid tenantId = Guid.CreateVersion7();
            await using ExploreDbContext winnerContext = CreateContext(databasePath, tenantId);
            var contenderGate = new IdempotencyInsertGate();
            await using ExploreDbContext contenderContext = CreateContext(databasePath, tenantId, contenderGate);
            var winnerRepository = new IdempotencyRepository(winnerContext);
            var contenderRepository = new IdempotencyRepository(contenderContext);
            await using var winnerTransaction = await winnerContext.Database.BeginTransactionAsync();
            IdempotencyRecord winnerRecord = NewClaim("contended-key", tenantId);
            IdempotencyRecord contenderRecord = NewClaim("contended-key", tenantId);

            IdempotencyClaim winner = await winnerRepository.TryClaimAsync(winnerRecord);
            Task<IdempotencyClaim> contenderTask = contenderRepository.TryClaimAsync(contenderRecord);
            await contenderGate.WaitUntilCommandReachedAsync();
            await Assert.That(contenderTask.IsCompleted).IsFalse();

            contenderGate.Release();
            await winnerTransaction.CommitAsync();
            IdempotencyClaim contender = await contenderTask;

            await Assert.That(winner.IsOwner).IsTrue();
            await Assert.That(contender.IsOwner).IsFalse();
            await Assert.That(contender.Record.Id).IsEqualTo(winnerRecord.Id);
            await using ExploreDbContext verification = CreateContext(databasePath);
            await Assert.That(await verification.IdempotencyRecords.CountAsync(record =>
                record.Key == winnerRecord.Key &&
                record.TenantId == tenantId)).IsEqualTo(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    [Test]
    public async Task ExpiredClaimRetainsIdentityAndCannotAcquireAnotherOwner()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"idempotency-expiry-{Guid.CreateVersion7():N}.db");
        try
        {
            Guid tenantId = Guid.CreateVersion7();
            await using ExploreDbContext context = CreateContext(databasePath, tenantId);
            await context.Database.EnsureCreatedAsync();
            await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
            var repository = new IdempotencyRepository(context);
            IdempotencyRecord expired = NewClaim("replace-key", tenantId);
            expired.CreatedAt = Now.AddDays(-2);
            expired.ExpiresAt = Now.AddMinutes(-1);
            context.IdempotencyRecords.Add(expired);
            await context.SaveChangesAsync();
            IdempotencyRecord replacement = NewClaim("replace-key", tenantId);

            IdempotencyClaim replaced = await repository.TryClaimAsync(replacement);
            IdempotencyClaim replay = await repository.TryClaimAsync(NewClaim("replace-key", tenantId));

            await Assert.That(replaced.IsOwner).IsFalse();
            await Assert.That(replaced.Record.Id).IsEqualTo(expired.Id);
            await Assert.That(replaced.Record.StatusCode).IsEqualTo(IdempotencyRecord.ResponseUnavailableStatusCode);
            await Assert.That(replay.IsOwner).IsFalse();
            await Assert.That(replay.Record.Id).IsEqualTo(expired.Id);
            await Assert.That(await context.IdempotencyRecords.CountAsync(record =>
                record.Key == replacement.Key &&
                record.TenantId == tenantId)).IsEqualTo(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    [Test]
    public async Task SameTransactionRollbackRemovesReceiptAndEffectButUnrelatedRollbackRetainsFence()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"idempotency-rollback-{Guid.CreateVersion7():N}.db");
        try
        {
            Guid tenantId = Guid.CreateVersion7();
            await using ExploreDbContext context = CreateContext(databasePath, tenantId);
            await context.Database.EnsureCreatedAsync();
            await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
            var repository = new IdempotencyRepository(context, new ReceiptClock());
            IdempotencyRecord claim = NewClaim("rolled-back", tenantId);
            await context.Database.ExecuteSqlRawAsync("CREATE TABLE rollback_effects (id TEXT PRIMARY KEY)");
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                var acquisition = await repository.TryClaimAsync(claim);
                using var owner = acquisition.Owner;
                await Assert.That(acquisition.IsOwner).IsTrue();
                await context.Database.ExecuteSqlRawAsync("INSERT INTO rollback_effects (id) VALUES ('owned-effect')");
                await transaction.RollbackAsync();
            }
            await Assert.That(await context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM rollback_effects").SingleAsync()).IsEqualTo(0);
            await Assert.That(await context.IdempotencyRecords.CountAsync()).IsEqualTo(0);
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                IdempotencyClaim retry = await repository.TryClaimAsync(NewClaim("rolled-back", tenantId));
                using var owner = retry.Owner;
                await Assert.That(retry.IsOwner).IsTrue();
                await context.Database.ExecuteSqlRawAsync("INSERT INTO rollback_effects (id) VALUES ('owned-effect')");
                await Assert.That(await repository.CompleteAsync(owner!, 201, "committed", "text/plain")).IsTrue();
                await transaction.CommitAsync();
            }
            await Assert.That(await context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM rollback_effects").SingleAsync()).IsEqualTo(1);
            await Assert.That(await context.IdempotencyRecords.CountAsync()).IsEqualTo(1);

            var durable = await repository.TryClaimAsync(NewClaim("durable", tenantId));
            using var durableOwner = durable.Owner;
            await Assert.That(durable.IsOwner).IsTrue();
            await using (var unrelated = await context.Database.BeginTransactionAsync())
            {
                await context.Database.ExecuteSqlRawAsync("INSERT INTO rollback_effects (id) VALUES ('unrelated-effect')");
                await unrelated.RollbackAsync();
            }
            var fenced = await repository.TryClaimAsync(NewClaim("durable", tenantId));
            await Assert.That(fenced.IsOwner).IsFalse();
            await Assert.That(await context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM rollback_effects WHERE id = 'unrelated-effect'").SingleAsync()).IsEqualTo(0);
            await Assert.That(await context.IdempotencyRecords.CountAsync()).IsEqualTo(2);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    private sealed class ReceiptClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    [Test]
    public async Task SelectedSqliteTenantCannotReadClaimOrConsumeForeignTenantReceipts()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"idempotency-tenant-{Guid.CreateVersion7():N}.db");
        try
        {
            Guid tenantA = Guid.CreateVersion7(), tenantB = Guid.CreateVersion7();
            await using var foreign = CreateContext(databasePath, tenantB);
            await foreign.Database.EnsureCreatedAsync();
            await SqliteDatabaseInitializer.InitializeAsync(foreign, CancellationToken.None);
            var foreignRepository = new IdempotencyRepository(foreign, new ReceiptClock());
            var proposal = NewClaim("foreign-private", tenantB);
            var acquisition = await foreignRepository.TryClaimAsync(proposal);
            using var owner = acquisition.Owner!;
            await Assert.That(await foreignRepository.CompleteAsync(owner, 201, "private", "text/plain")).IsTrue();
            await using var selected = CreateContext(databasePath, tenantA);
            var repository = new IdempotencyRepository(selected, new ReceiptClock());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                repository.LookupAsync(IdempotencyRequestDescriptor.FromRecord(proposal), Now));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                repository.TryClaimAsync(NewClaim("foreign-fresh", tenantB)));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                new AtprotoBootstrapReplayRepository(selected, new ReceiptClock())
                    .TryConsumeAsync("foreign-bootstrap", tenantB, new DateTimeOffset(Now.AddHours(1))));
            await Assert.That(await foreign.IdempotencyRecords.CountAsync()).IsEqualTo(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    [Test]
    public async Task CompletionRequiresOriginalIssuerUnspentHandleAndUnbrokenConnection()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"idempotency-owner-{Guid.CreateVersion7():N}.db");
        try
        {
            Guid tenant = Guid.CreateVersion7();
            await using ExploreDbContext context = CreateContext(databasePath, tenant);
            await context.Database.EnsureCreatedAsync();
            await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
            var repository = new IdempotencyRepository(context, new ReceiptClock());
            var otherIssuer = new IdempotencyRepository(context, new ReceiptClock());
            var claim = await repository.TryClaimAsync(NewClaim("original-owner", tenant));
            using (var owner = claim.Owner!)
            {
                var copied = await repository.TryClaimAsync(NewClaim("original-owner", tenant));
                await Assert.That(copied.IsOwner).IsFalse();
                await Assert.That(copied.Owner).IsNull();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    otherIssuer.CompleteAsync(owner, 201, "forged", "text/plain"));
                await Assert.That(await repository.CompleteAsync(owner, 201, "original", "text/plain")).IsTrue();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    repository.CompleteAsync(owner, 202, "replacement", "text/plain"));
            }
            var interrupted = await repository.TryClaimAsync(NewClaim("interrupted-owner", tenant));
            using var interruptedOwner = interrupted.Owner!;
            await context.Database.CloseConnectionAsync();
            await context.Database.OpenConnectionAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                repository.CompleteAsync(interruptedOwner, 201, "reopened", "text/plain"));
            var fenced = await repository.TryClaimAsync(NewClaim("interrupted-owner", tenant));
            await Assert.That(fenced.IsOwner).IsFalse();
            await Assert.That(fenced.Record!.StatusCode).IsEqualTo(IdempotencyRecord.InProgressStatusCode);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    [Test]
    public async Task ExpiryScrubsReceiptButRetainsEveryPotentiallyCommittedFence()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"idempotency-fence-{Guid.CreateVersion7():N}.db");
        try
        {
            Guid tenantId = Guid.CreateVersion7();
            await using ExploreDbContext context = CreateContext(databasePath, tenantId);
            await context.Database.EnsureCreatedAsync();
            await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
            var repository = new IdempotencyRepository(context);
            foreach (int status in new[] { 0, 201, IdempotencyRecord.ResponseUnavailableStatusCode })
            {
                IdempotencyRecord record = NewClaim($"expired-{status}", tenantId);
                record.StatusCode = status;
                record.ResponseBody = status == 201 ? "{\"receipt\":true}" : null;
                record.ExpiresAt = Now.AddDays(-1);
                context.IdempotencyRecords.Add(record);
            }
            await context.SaveChangesAsync();
            var scopes = new IdempotencyCleanupExecutionScopeFactory(new CleanupContextFactory(databasePath));
            await using (var scope = await scopes.CreateAsync(tenantId))
            {
                await Assert.That(await scope.CountExpiredAsync(Now, 10)).IsEqualTo(2);
                await Assert.That(await scope.ScrubExpiredAsync(Now, 10)).IsEqualTo(2);
                await Assert.That(await scope.CountExpiredAsync(Now, 10)).IsEqualTo(0);
            }
            foreach (int status in new[] { 0, 201, IdempotencyRecord.ResponseUnavailableStatusCode })
            {
                IdempotencyClaim retry = await repository.TryClaimAsync(NewClaim($"expired-{status}", tenantId));
                await Assert.That(retry.IsOwner).IsFalse();
                await Assert.That(retry.Record.StatusCode).IsEqualTo(IdempotencyRecord.ResponseUnavailableStatusCode);
                await Assert.That(retry.Record.ResponseBody).IsNull();
            }
            await Assert.That(await context.IdempotencyRecords.CountAsync()).IsEqualTo(3);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task CleanupDiscoveryAndTenantScopesShareOnePassBudgetWithoutDirectoryRows(bool dryRun)
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"idempotency-cleanup-{Guid.CreateVersion7():N}.db");
        try
        {
            Guid first = Guid.CreateVersion7();
            Guid second = Guid.CreateVersion7();
            Guid outsidePage = Guid.CreateVersion7();
            await using var setup = CreateContext(databasePath);
            await setup.Database.EnsureCreatedAsync();
            await SqliteDatabaseInitializer.InitializeAsync(setup, CancellationToken.None);
            foreach (var (key, tenant, hours, status) in new[]
            {
                ("old-fence", outsidePage, -100, IdempotencyRecord.ResponseUnavailableStatusCode),
                ("first-old", first, -5, 201),
                ("second-old", second, -4, 201),
                ("first-new", first, -3, 0),
                ("second-new", second, -2, 201),
                ("outside-page", outsidePage, -1, 201),
                ("live", first, 1, 201)
            })
            {
                var record = NewClaim(key, tenant);
                record.ExpiresAt = Now.AddHours(hours);
                record.StatusCode = status;
                record.ResponseBody = status == 201 ? "{}" : null;
                record.ContentType = status == 201 ? "application/json" : null;
                setup.IdempotencyRecords.Add(record);
            }
            await setup.SaveChangesAsync();

            var contexts = new CleanupContextFactory(databasePath);
            var discovery = new IdempotencyCleanupTenantDiscoveryRepository(contexts);
            var tenants = await discovery.GetExpiredTenantIdsAsync(Now, 3);
            await Assert.That(tenants.Count).IsEqualTo(2);
            await Assert.That(tenants[0]).IsEqualTo(first);
            await Assert.That(tenants[1]).IsEqualTo(second);
            var scopes = new IdempotencyCleanupExecutionScopeFactory(contexts);
            using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
            using var metrics = new BusinessMetrics(services.GetRequiredService<IMeterFactory>());
            var service = new IdempotencyCleanupService(discovery, scopes,
                Options.Create(new IdempotencyCleanupSettings
                {
                    BatchSize = 3,
                    ExpirationGraceHours = 0,
                    DryRun = dryRun
                }), metrics, NullLogger<IdempotencyCleanupService>.Instance);
            var result = await service.CleanupExpiredAsync(Now);

            await Assert.That(result.EligibleCount).IsEqualTo(3);
            await Assert.That(result.ScrubbedCount).IsEqualTo(dryRun ? 0 : 3);
            await Assert.That(result.DryRun).IsEqualTo(dryRun);
            await Assert.That(result.ExpiresBeforeUtc).IsEqualTo(Now);
            await using var view = CreateContext(databasePath);
            await Assert.That(await view.IdempotencyRecords.CountAsync()).IsEqualTo(7);
            await Assert.That(await view.IdempotencyRecords.CountAsync(row =>
                row.StatusCode == IdempotencyRecord.ResponseUnavailableStatusCode)).IsEqualTo(dryRun ? 1 : 4);
            foreach (string key in new[] { "second-new", "outside-page", "live" })
            {
                var record = await view.IdempotencyRecords.AsNoTracking().SingleAsync(row => row.Key == key);
                await Assert.That(record.StatusCode).IsEqualTo(201);
                await Assert.That(record.ResponseBody).IsEqualTo("{}");
            }
            if (!dryRun)
            {
                foreach (string key in new[] { "first-old", "second-old", "first-new" })
                {
                    var record = await view.IdempotencyRecords.AsNoTracking().SingleAsync(row => row.Key == key);
                    await Assert.That(record.ResponseBody).IsNull();
                    await Assert.That(record.ContentType).IsNull();
                    await Assert.That(record.StatusCode).IsEqualTo(IdempotencyRecord.ResponseUnavailableStatusCode);
                }
            }
            await Assert.That(setup.IsTenantFilterBypassed).IsTrue();
            await Assert.That(setup.TenantContext).IsNull();
            await Assert.That(contexts.RuntimeTenants.Count).IsEqualTo(2);
            await Assert.That(contexts.RuntimeTenants[0]).IsEqualTo(first);
            await Assert.That(contexts.RuntimeTenants[1]).IsEqualTo(second);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    [Test]
    public async Task CleanupRejectsBypassedOrWrongTenantContextsBeforeReadingReceipts()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"idempotency-custody-{Guid.CreateVersion7():N}.db");
        var bypassed = new InvalidCleanupContextFactory(databasePath, bypass: true);
        var wrongTenant = new InvalidCleanupContextFactory(databasePath, bypass: false);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new IdempotencyCleanupExecutionScopeFactory(bypassed).CreateAsync(Guid.CreateVersion7()));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new IdempotencyCleanupExecutionScopeFactory(wrongTenant).CreateAsync(Guid.CreateVersion7()));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new IdempotencyCleanupTenantDiscoveryRepository(bypassed).GetExpiredTenantIdsAsync(Now, 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await new IdempotencyCleanupTenantDiscoveryRepository(bypassed)
                .GetExpiredTenantIdsAsync(Now, IdempotencyCleanupLimits.MaximumRows + 1));
    }

    private sealed class CleanupContextFactory(string databasePath)
        : IGlobalExploreDbContextFactory, ITenantExploreDbContextFactory
    {
        public List<Guid> RuntimeTenants { get; } = [];

        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = CreateContext(databasePath);
            context.ClearTenantFilterBypass();
            return Task.FromResult(context);
        }

        public async Task<ExploreDbContext> CreateDbContextAsync(
            Guid tenantId, CancellationToken cancellationToken = default)
        {
            var context = await CreateDbContextAsync(cancellationToken);
            context.TenantContext = new CleanupTenant(tenantId);
            context.BindTenantLease();
            RuntimeTenants.Add(tenantId);
            return context;
        }
    }

    private sealed class InvalidCleanupContextFactory(string databasePath, bool bypass)
        : IGlobalExploreDbContextFactory, ITenantExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = CreateContext(databasePath);
            if (!bypass)
            {
                context.ClearTenantFilterBypass();
                context.TenantContext = new CleanupTenant(Guid.CreateVersion7());
                context.BindTenantLease();
            }
            return Task.FromResult(context);
        }

        public Task<ExploreDbContext> CreateDbContextAsync(
            Guid tenantId, CancellationToken cancellationToken = default) => CreateDbContextAsync(cancellationToken);
    }

    private sealed record CleanupTenant(Guid TenantId) : ITenantContext;

    private static ExploreDbContext CreateContext(
        string databasePath,
        params IInterceptor[] interceptors)
    {
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            DefaultTimeout = 30,
            ForeignKeys = true,
            Pooling = true
        }.ToString();
        var builder = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                SqliteNamedLockTransactionInterceptor.Instance,
                SqliteProjectionLockTransactionInterceptor.Instance);
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        var context = new ExploreDbContext(builder.Options);
        context.EnableTenantFilterBypass("Idempotency SQLite contention contract.");
        return context;
    }

    private static ExploreDbContext CreateContext(
        string databasePath, Guid tenantId, params IInterceptor[] interceptors)
    {
        var context = CreateContext(databasePath, interceptors);
        context.ClearTenantFilterBypass();
        context.TenantContext = new CleanupTenant(tenantId);
        context.BindTenantLease();
        return context;
    }

    private static IdempotencyRecord NewClaim(string key, Guid tenantId) =>
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
            CreatedAt = Now,
            ExpiresAt = Now.AddHours(24)
        };

    private sealed class IdempotencyInsertGate : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _commandReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _armed = 1;

        public Task WaitUntilCommandReachedAsync() =>
            _commandReached.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void Release() => _release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await WaitAtInsertAsync(command, cancellationToken);

            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await WaitAtInsertAsync(command, cancellationToken);
            return result;
        }

        private async Task WaitAtInsertAsync(
            DbCommand command,
            CancellationToken cancellationToken)
        {
            if (command.CommandText.Contains("idempotency_records", StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref _armed, 0) == 1)
            {
                _commandReached.TrySetResult();
                await _release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
        }
    }
}
